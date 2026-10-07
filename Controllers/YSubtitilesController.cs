using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.Extensions;
using YoutubeExplode.Videos;
using YoutubeDownload.Services; // <-- добавили
using System.Text;
using System.Text.RegularExpressions;

public class UpdateResultDto
{
    public string Result { get; set; } = string.Empty;
}

public class UpdateVisibilityDto
{
    public YoutubeCaptionVisibility Visibility { get; set; }
}

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class YSubtitilesController : ControllerBase
    {
        private readonly ICaptionTaskManager _taskManager;
        private readonly IYoutubeCaptionService _youtubeCaptionService;
        private readonly IDocumentGeneratorService _pdfGeneratorService;
        private readonly IYSubtitlesService _ySubtitlesService;
        private readonly YoutubeWorkflowService _workflow; // <-- добавили
        private readonly ISubscriptionService _subscriptionService;
        private readonly MyDbContext _dbContext;
        private readonly CaptionService _captionService;

        public YSubtitilesController(
            ICaptionTaskManager taskManager,
            IYoutubeCaptionService youtubeCaptionService,
            IDocumentGeneratorService pdfGeneratorService,
            IYSubtitlesService ySubtitlesService,
            YoutubeWorkflowService workflow, // <-- добавили
            MyDbContext dbContext,
            ISubscriptionService subscriptionService,
            CaptionService captionService
        )
        {
            _taskManager = taskManager;
            _youtubeCaptionService = youtubeCaptionService;
            _pdfGeneratorService = pdfGeneratorService;
            _ySubtitlesService = ySubtitlesService;
            _workflow = workflow; // <-- добавили
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _captionService = captionService;
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<YoutubeCaptionTaskDto>> GetStatus(string taskId)
        {
            var entity = await FindVisibleTaskAsync(taskId);
            if (entity == null)
                return NotFound("Task not found.");

            var task = await _taskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound("Task not found.");

            return Ok(task);
        }


        [HttpDelete("{taskId}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> DeleteTask(string taskId)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var deleted = await _taskManager.DeleteTaskAsync(taskId, userId, User.IsInRole("Admin"));
            if (!deleted)
                return NotFound("Task not found.");

            return NoContent();
        }

        [HttpPost("{taskId}/restart")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<YoutubeCaptionTaskDto>> RestartTask(string taskId)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("User is not authenticated");

            try
            {
                var restarted = await _taskManager.RestartCaptionTaskAsync(
                    taskId,
                    userId,
                    User.IsInRole("Admin"));

                if (restarted == null)
                    return NotFound("Task not found.");

                return Ok(restarted);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpPut("{taskId}/result")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateResult(string taskId, [FromBody] UpdateResultDto? dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Result))
                return BadRequest("Result must be provided.");

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var updated = await _taskManager.UpdateTaskResultAsync(taskId, dto.Result, userId, User.IsInRole("Admin"));
            if (!updated)
                return NotFound("Task not found.");

            return NoContent();
        }

        [HttpPut("{taskId}/visibility")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateVisibility(
            string taskId,
            [FromBody] UpdateVisibilityDto dto,
            CancellationToken cancellationToken)
        {
            if (dto == null)
                return BadRequest("Visibility must be provided.");

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("User is not authenticated");

            var task = await _ySubtitlesService.GetTaskByIdAsync(taskId);
            if (task == null)
                return NotFound("Task not found.");

            var isAdmin = User.IsInRole("Admin");
            var isOwner = string.Equals(task.UserId, userId, StringComparison.Ordinal);

            var canHide = bool.TryParse(User.FindFirstValue("subscriptionCanHideCaptions"), out var parsed) && parsed;
            if (!isAdmin && (!isOwner || !canHide))
                return Forbid();

            var updated = await _ySubtitlesService.UpdateVisibilityAsync(task.Id, dto.Visibility, cancellationToken)
                .ConfigureAwait(false);

            if (!updated)
                return NotFound("Task not found.");

            return NoContent();
        }

        [HttpGet("all")]
        public async Task<ActionResult<List<YoutubeCaptionTaskDto>>> GetAllTasks()
        {
            var tasks = await _ySubtitlesService.GetAllTasksAsync();
            return Ok(tasks.Select(t => new YoutubeCaptionTaskDto
            {
                Id = t.Id,
                VideoId = t.VideoId ?? t.Id,
                CaptionTrackKey = t.CaptionTrackKey,
                Slug = t.Slug ?? t.Id,
                Title = t.Title,
                ChannelName = t.ChannelName,
                ChannelId = t.ChannelId,
                Result = t.Result,
                Error = t.Error,
                Status = t.Status,
                Done = t.Done,
                SegmentsTotal = t.SegmentsTotal,
                SegmentsProcessed = t.SegmentsProcessed,
                CreatedAt = t.CreatedAt,
                ModifiedAt = t.ModifiedAt,
                UploadDate = t.UploadDate
            }).ToList());
        }

        [HttpGet("Titles")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        public async Task<ActionResult> Titles()
        {
            await _youtubeCaptionService.UpdateNullTitlesAsync();
            return Ok();
        }

        [HttpGet("GetTasks")]
        public async Task<IActionResult> GetTasks(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortField = null,
            [FromQuery] string sortOrder = null,
            [FromQuery] string filter = null,
            [FromQuery] string userId = null,
            [FromQuery] bool includeHidden = false)
        {
            var currentUserId = User.GetUserId();
            var isAdmin = User.IsInRole("Admin");
            if (!string.IsNullOrEmpty(userId) && userId != currentUserId && !isAdmin)
                return Forbid();
            if (includeHidden && string.IsNullOrEmpty(userId) && !isAdmin)
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _ySubtitlesService.GetTasksPagedAsync(
                page, pageSize, sortField, sortOrder, filter, userId, includeHidden);

            var result = new
            {
                items,
                totalCount
            };
            return Ok(result);
        }

        [HttpGet("GetAllTasksTable")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        public async Task<IActionResult> GetAllTasksTable()
        {
            var items = await _ySubtitlesService.GetAllTasksTableAsync();
            return Ok(items);
        }

        [HttpGet("Slug")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        public async Task Slug()
        {
            await _ySubtitlesService.PopulateSlugsAsync();
        }

        [HttpGet("GenerateSrt/{taskId}")]
        [Produces("application/x-subrip")]
        public async Task<IActionResult> GenerateSrt(string taskId, [FromQuery] string? lang = null)
        {
            var visibleTask = await FindVisibleTaskAsync(taskId);
            if (visibleTask == null)
                return NotFound("Task not found.");

            try
            {
                // Генерация SRT из JSON субтитров, лежащего в YoutubeCaptionTexts.Caption
                var srtPath = await _pdfGeneratorService.GenerateSrtFromDbJsonAsync(visibleTask.Id, lang);

                if (!System.IO.File.Exists(srtPath))
                    return NotFound("SRT file not found.");

                try
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(srtPath);
                    var fileName = System.IO.Path.GetFileName(srtPath);
                    return File(fileBytes, "application/x-subrip", fileName);
                }
                finally
                {
                    System.IO.File.Delete(srtPath);
                }
            }
            catch (Exception ex)
            {
                // Поведение как в GeneratePdf/GenerateWord — 500 на неожиданные ошибки
                return StatusCode(500, ex.Message);
            }
        }



        [HttpGet("GenerateWord/{taskId}")]
        public async Task<IActionResult> GenerateWord(string taskId)
        {
            var visibleTask = await FindVisibleTaskAsync(taskId);
            if (visibleTask == null) return NotFound("Task not found.");

            var task = await _taskManager.GetTaskStatusAsync(visibleTask.Id);
            if (task == null)
                return NotFound("Task not found.");

            if (string.IsNullOrWhiteSpace(task.Result))
                return BadRequest("Task does not contain any Markdown content in 'Result' field.");

            try
            {
                var wordPath = await _pdfGeneratorService.GenerateWordFromMarkdownAsync(task.Id, task.Result);
                if (!System.IO.File.Exists(wordPath))
                    return NotFound("DOCX file not found.");

                try
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(wordPath);
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", GetExportFileName(task.Title, "docx"));
                }
                finally
                {
                    System.IO.File.Delete(wordPath);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("GeneratePdf/{taskId}")]
        public async Task<IActionResult> GeneratePdf(string taskId)
        {
            var visibleTask = await FindVisibleTaskAsync(taskId);
            if (visibleTask == null) return NotFound("Task not found.");

            var task = await _taskManager.GetTaskStatusAsync(visibleTask.Id);
            if (task == null)
                return NotFound("Task not found.");

            if (string.IsNullOrWhiteSpace(task.Result))
                return BadRequest("Task does not contain any Markdown content in 'Result' field.");

            try
            {
                var pdfPath = await _pdfGeneratorService.GeneratePdfFromMarkdownAsync(task.Id, task.Result);
                if (!System.IO.File.Exists(pdfPath))
                {
                    return NotFound("PDF file not found.");
                }

                try
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
                    return File(fileBytes, "application/pdf", GetExportFileName(task.Title, "pdf"));
                }
                finally
                {
                    System.IO.File.Delete(pdfPath);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private static string GetExportFileName(string? title, string extension)
        {
            var safeTitle = Regex.Replace(title ?? string.Empty, @"[^\p{L}\p{N} _-]", "_").Trim();
            if (safeTitle.Length > 80)
                safeTitle = safeTitle[..80];
            return $"task-{(safeTitle.Length == 0 ? "document" : safeTitle)}.{extension}";
        }

        private async Task<YoutubeCaptionTask?> FindVisibleTaskAsync(string taskId)
        {
            var query = _dbContext.YoutubeCaptionTasks.AsNoTracking();
            var task = await query.FirstOrDefaultAsync(t => t.Slug == taskId)
                ?? await query.FirstOrDefaultAsync(t => t.PreviousSlug == taskId)
                ?? await query.FirstOrDefaultAsync(t => t.Id == taskId);
            if (task == null || task.Visibility == YoutubeCaptionVisibility.Deleted)
                return null;

            if (task.Visibility == YoutubeCaptionVisibility.Hidden)
            {
                var currentUserId = User.GetUserId();
                var isOwner = currentUserId != null && task.UserId == currentUserId;
                if (!isOwner && !User.IsInRole("Admin"))
                    return null;
            }

            return task;
        }

        [HttpGet("tracks")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<IReadOnlyList<CaptionTrackOption>>> GetCaptionTracks(
            [FromQuery] string youtubeId)
        {
            var videoId = VideoId.TryParse(youtubeId);
            if (videoId is null)
                return BadRequest("Invalid YouTube video ID.");

            var tracks = await _captionService.GetAvailableSubtitlesAsync(videoId.Value);
            return Ok(CaptionTrackSelection.Describe(tracks));
        }

        [HttpPost("start")]        
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<StartSubtitleRecognitionResponse>> StartSubtitleRecognition(
            [FromQuery] string youtubeId,
            [FromQuery] string? language,
            [FromQuery] string? trackKey,
            CancellationToken cancellationToken
        )
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return Unauthorized("not authenticated");

            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            var clientIp = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UnknownIP";

            var videoId = VideoId.TryParse(youtubeId);
            if (videoId is null)
                return BadRequest("Invalid YouTube video ID.");
            youtubeId = videoId.Value;

            if (!string.IsNullOrWhiteSpace(trackKey))
            {
                var tracks = await _captionService.GetAvailableSubtitlesAsync(youtubeId);
                if (CaptionTrackSelection.Find(tracks, trackKey) is null)
                    return BadRequest("Selected subtitle track is not available.");
            }
            else if (IsRequestedLanguage(language))
            {
                var tracks = await _captionService.GetAvailableSubtitlesAsync(youtubeId);
                trackKey = CaptionTrackSelection.Describe(tracks)
                    .FirstOrDefault(option => string.Equals(
                        option.LanguageCode, language, StringComparison.OrdinalIgnoreCase))?.Key;
                if (trackKey is null)
                    return BadRequest("Requested subtitle language is not available.");
            }
            else
            {
                trackKey = null;
            }

            var taskId = await _taskManager.EnqueueCaptionTaskAsync(
                youtubeId,
                clientIp,
                userId,
                trackKey
            );

            var balance = await _subscriptionService
                .GetQuotaBalanceAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var remainingVideos = NormalizeRemaining(balance.RemainingVideos);
            var remainingMinutes = NormalizeRemaining(balance.RemainingTranscriptionMinutes);

            var response = new StartSubtitleRecognitionResponse
            {
                TaskId = taskId,
                RemainingQuota = remainingVideos,
                RemainingTranscriptionMinutes = remainingMinutes,
                RemainingVideos = remainingVideos
            };

            return Ok(response);
        }

        [HttpPost("start-batch")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<StartSubtitleRecognitionBatchResponse>> StartSubtitleRecognitionBatch(
            [FromBody] StartSubtitleRecognitionBatchRequest request,
            CancellationToken cancellationToken
        )
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return Unauthorized("not authenticated");

            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            if (request == null || request.YoutubeIds == null || request.YoutubeIds.Count == 0)
                return BadRequest("YoutubeIds must be provided.");

            var clientIp = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UnknownIP";

            var rawItems = request.YoutubeIds
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .SelectMany(item => item.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rawItems.Count == 0)
                return BadRequest("YoutubeIds must be provided.");

            var taskIds = new List<string>();
            var invalidItems = new List<string>();
            int? remainingQuota = null;
            int? remainingMinutes = null;
            int? remainingVideos = null;

            foreach (var item in rawItems)
            {
                var parsed = VideoId.TryParse(item);
                if (parsed is null)
                {
                    invalidItems.Add(item);
                    continue;
                }

                var normalizedId = parsed.ToString();

                string? trackKey = null;
                if (request.TrackKeys != null &&
                    (request.TrackKeys.TryGetValue(item, out trackKey) ||
                     request.TrackKeys.TryGetValue(normalizedId, out trackKey)) &&
                    !string.IsNullOrWhiteSpace(trackKey))
                {
                    var tracks = await _captionService.GetAvailableSubtitlesAsync(normalizedId);
                    if (CaptionTrackSelection.Find(tracks, trackKey) is null)
                    {
                        invalidItems.Add(item);
                        continue;
                    }
                }
                else
                {
                    trackKey = null;
                }

                if (trackKey is null && IsRequestedLanguage(request.Language))
                {
                    var tracks = await _captionService.GetAvailableSubtitlesAsync(normalizedId);
                    trackKey = CaptionTrackSelection.Describe(tracks)
                        .FirstOrDefault(option => string.Equals(
                            option.LanguageCode, request.Language, StringComparison.OrdinalIgnoreCase))?.Key;
                    if (trackKey is null)
                    {
                        invalidItems.Add(item);
                        continue;
                    }
                }

                var taskId = await _taskManager.EnqueueCaptionTaskAsync(
                    normalizedId,
                    clientIp,
                    userId,
                    trackKey
                );

                taskIds.Add(taskId);
            }

            var balance = await _subscriptionService
                .GetQuotaBalanceAsync(userId, cancellationToken)
                .ConfigureAwait(false);
            remainingVideos = NormalizeRemaining(balance.RemainingVideos);
            remainingMinutes = NormalizeRemaining(balance.RemainingTranscriptionMinutes);
            remainingQuota = remainingVideos;

            var response = new StartSubtitleRecognitionBatchResponse
            {
                TaskIds = taskIds,
                InvalidItems = invalidItems,
                RemainingQuota = remainingQuota,
                RemainingTranscriptionMinutes = remainingMinutes,
                RemainingVideos = remainingVideos
            };

            return Ok(response);
        }

        private static int? NormalizeRemaining(int remaining)
        {
            return remaining == int.MaxValue
                ? null
                : Math.Max(0, remaining);
        }

        private static bool IsRequestedLanguage(string? language)
            => !string.IsNullOrWhiteSpace(language)
               && !string.Equals(language, "user", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(language, "system", StringComparison.OrdinalIgnoreCase);
    }
}
