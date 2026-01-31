using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        private readonly ISubscriptionAccessService _subscriptionAccessService;
        private readonly ISubscriptionService _subscriptionService;

        public YSubtitilesController(
            ICaptionTaskManager taskManager,
            IYoutubeCaptionService youtubeCaptionService,
            IDocumentGeneratorService pdfGeneratorService,
            IYSubtitlesService ySubtitlesService,
            YoutubeWorkflowService workflow, // <-- добавили
            ISubscriptionAccessService subscriptionAccessService,
            ISubscriptionService subscriptionService
        )
        {
            _taskManager = taskManager;
            _youtubeCaptionService = youtubeCaptionService;
            _pdfGeneratorService = pdfGeneratorService;
            _ySubtitlesService = ySubtitlesService;
            _workflow = workflow; // <-- добавили
            _subscriptionAccessService = subscriptionAccessService ?? throw new ArgumentNullException(nameof(subscriptionAccessService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<YoutubeCaptionTask>> GetStatus(string taskId)
        {
            var task = await _taskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound("Task not found.");

            return Ok(task);
        }


        [HttpDelete("{taskId}")]
        public async Task<IActionResult> DeleteTask(string taskId)
        {
            var deleted = await _taskManager.DeleteTaskAsync(taskId);
            if (!deleted)
                return NotFound("Task not found.");

            return NoContent();
        }

        [HttpPut("{taskId}/result")]
        public async Task<IActionResult> UpdateResult(string taskId, [FromBody] UpdateResultDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Result))
                return BadRequest("Result must be provided.");

            var updated = await _taskManager.UpdateTaskResultAsync(taskId, dto.Result);
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
        public async Task<ActionResult<List<YoutubeCaptionTask>>> GetAllTasks()
        {
            var tasks = await _ySubtitlesService.GetAllTasksAsync();
            return Ok(tasks);
        }

        [HttpGet("Titles")]
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
        public async Task<IActionResult> GetAllTasksTable()
        {
            var items = await _ySubtitlesService.GetAllTasksTableAsync();
            return Ok(items);
        }

        [HttpGet("Slug")]
        public async Task Slug()
        {
            await _ySubtitlesService.PopulateSlugsAsync();
        }

        [HttpGet("GenerateSrt/{taskId}")]
        [Produces("application/x-subrip")]
        public async Task<IActionResult> GenerateSrt(string taskId, [FromQuery] string? lang = null)
        {
            // Для一致ности с другими методами сначала проверим наличие задачи
            var task = await _taskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound("Task not found.");

            try
            {
                // Генерация SRT из JSON субтитров, лежащего в YoutubeCaptionTexts.Caption
                var srtPath = await _pdfGeneratorService.GenerateSrtFromDbJsonAsync(taskId, lang);

                if (!System.IO.File.Exists(srtPath))
                    return NotFound("SRT file not found.");

                var fileBytes = await System.IO.File.ReadAllBytesAsync(srtPath);
                var fileName = System.IO.Path.GetFileName(srtPath);

                // Если нужны временные файлы — оставляем; иначе можно удалить после чтения
                // System.IO.File.Delete(srtPath);

                return File(fileBytes, "application/x-subrip", fileName);
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
            var task = await _taskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound("Task not found.");

            if (string.IsNullOrWhiteSpace(task.Result))
                return BadRequest("Task does not contain any Markdown content in 'Result' field.");

            try
            {
                var wordPath = await _pdfGeneratorService.GenerateWordFromMarkdownAsync(task.Id, task.Result);
                if (!System.IO.File.Exists(wordPath))
                    return NotFound("DOCX file not found.");

                var fileBytes = await System.IO.File.ReadAllBytesAsync(wordPath);
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"task-{task.Title}.docx");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("GeneratePdf/{taskId}")]
        public async Task<IActionResult> GeneratePdf(string taskId)
        {
            var task = await _taskManager.GetTaskStatusAsync(taskId);
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

                var fileBytes = await System.IO.File.ReadAllBytesAsync(pdfPath);
                return File(fileBytes, "application/pdf", $"task-{task.Title}.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("start")]        
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<StartSubtitleRecognitionResponse>> StartSubtitleRecognition(
            [FromQuery] string youtubeId,
            [FromQuery] string? language,
            CancellationToken cancellationToken
        )
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return Unauthorized("not authenticated");

            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            var authorization = await _subscriptionAccessService
                .AuthorizeYoutubeRecognitionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            if (!authorization.IsAllowed)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new UsageLimitExceededResponse
                {
                    Message = authorization.Message ?? "Превышен лимит распознаваний.",
                    PaymentUrl = authorization.PaymentUrl ?? "/billing",
                    RemainingQuota = authorization.RemainingQuota,
                    RecognizedTitles = authorization.RecognizedTitles
                });
            }

            var clientIp = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UnknownIP";

            youtubeId = VideoId.Parse(youtubeId).Value;

            var taskId = await _taskManager.EnqueueCaptionTaskAsync(
                youtubeId,
                clientIp,
                userId
            );

            var response = new StartSubtitleRecognitionResponse
            {
                TaskId = taskId,
                RemainingQuota = authorization.RemainingQuota
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

            var hasLifetime = bool.TryParse(User.FindFirstValue("lifetimeAccess"), out var lifetime) && lifetime;
            var subscription = await _subscriptionService
                .GetActiveSubscriptionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            if (subscription == null && !hasLifetime)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new UsageLimitExceededResponse
                {
                    Message = "Добавление списком доступно только подписчикам.",
                    PaymentUrl = "/billing"
                });
            }

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

            foreach (var item in rawItems)
            {
                var parsed = VideoId.TryParse(item);
                if (parsed is null)
                {
                    invalidItems.Add(item);
                    continue;
                }

                var normalizedId = parsed.ToString();

                var authorization = await _subscriptionAccessService
                    .AuthorizeYoutubeRecognitionAsync(userId, cancellationToken)
                    .ConfigureAwait(false);

                if (!authorization.IsAllowed)
                {
                    return StatusCode(StatusCodes.Status402PaymentRequired, new UsageLimitExceededResponse
                    {
                        Message = authorization.Message ?? "Превышен лимит распознаваний.",
                        PaymentUrl = authorization.PaymentUrl ?? "/billing",
                        RemainingQuota = authorization.RemainingQuota,
                        RecognizedTitles = authorization.RecognizedTitles
                    });
                }

                var taskId = await _taskManager.EnqueueCaptionTaskAsync(
                    normalizedId,
                    clientIp,
                    userId
                );

                taskIds.Add(taskId);
                remainingQuota = authorization.RemainingQuota;
            }

            var response = new StartSubtitleRecognitionBatchResponse
            {
                TaskIds = taskIds,
                InvalidItems = invalidItems,
                RemainingQuota = remainingQuota
            };

            return Ok(response);
        }
    }
}
