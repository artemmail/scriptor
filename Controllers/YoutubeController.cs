using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using YandexSpeech.services;               // YoutubeStreamService
using YoutubeDownload.Services;           // IYoutubeDownloadTaskManager, YoutubeWorkflowService
using YoutubeDownload.Models;
using YoutubeDownload.Managers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;             // StreamDto, MergedVideoDto
using System.Text.RegularExpressions;
using YoutubeExplode.Videos;
using YandexSpeech.Extensions;

namespace YourNamespace.Controllers
{
    /// <summary>
    /// DTO для запроса на склейку (merge):  
    /// - VideoUrlOrId — URL или ID видео  
    /// - QualityLabel, Container — параметры видео  
    /// - AudioStreams — список аудиодорожек  
    /// </summary>
    public class MergeRequestDto
    {
        public string VideoUrlOrId { get; set; } = string.Empty;
        public string? QualityLabel { get; set; }
        public string? Container { get; set; }
        public List<StreamDto> AudioStreams { get; set; } = new();
    }

    [ApiController]
    [Route("api/[controller]")]
    public class YoutubeController : ControllerBase
    {
        private readonly YoutubeStreamService _youtubeStreamService;
        private readonly IYoutubeDownloadTaskManager _downloadTaskManager;
        private readonly YoutubeWorkflowService _workflowService;
        private readonly ILogger<YoutubeController> _logger;

        public YoutubeController(
            YoutubeStreamService youtubeStreamService,
            IYoutubeDownloadTaskManager downloadTaskManager,
            YoutubeWorkflowService workflowService,
            ILogger<YoutubeController> logger)
        {
            _youtubeStreamService = youtubeStreamService;
            _downloadTaskManager = downloadTaskManager;
            _workflowService = workflowService;
            _logger = logger;
        }

        /// <summary>
        /// GET api/youtube/streams?videoUrlOrId=...
        /// Возвращает все доступные потоки (audio, video, muxed) для указанного видео.
        /// </summary>
        [HttpGet("streams")]
        public async Task<ActionResult<List<StreamDto>>> GetAllStreams([FromQuery] string videoUrlOrId)
        {
            if (string.IsNullOrWhiteSpace(videoUrlOrId))
                return BadRequest("Параметр videoUrlOrId обязателен.");
            if (VideoId.TryParse(videoUrlOrId) is null)
                return BadRequest("Неверная ссылка или ID видео YouTube.");

            try
            {
                var streams = await _youtubeStreamService.GetAllStreamsAsync(videoUrlOrId);
                return Ok(streams);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list YouTube streams");
                return Problem("Не удалось получить список дорожек.", statusCode: 500);
            }
        }

        /// <summary>
        /// POST api/youtube/download?videoUrlOrId=...&type=...&qualityLabel=...&container=...
        /// Скачивает один поток напрямую (не через очередь).
        /// </summary>
        [HttpPost("download")]
        public async Task<IActionResult> DownloadStream(
            [FromQuery] string videoUrlOrId,
            [FromQuery] string type,
            [FromQuery] string? qualityLabel,
            [FromQuery] string? container)
        {
            if (string.IsNullOrWhiteSpace(videoUrlOrId) || string.IsNullOrWhiteSpace(type))
                return BadRequest("Параметры videoUrlOrId и type обязательны.");
            if (VideoId.TryParse(videoUrlOrId) is null)
                return BadRequest("Неверная ссылка или ID видео YouTube.");

            var typeLower = type.ToLowerInvariant();
            if (typeLower != "audio" && typeLower != "video" && typeLower != "muxed")
                return BadRequest("type должен быть 'audio', 'video' или 'muxed'.");

            // формируем уникальное имя файла
            var ext = string.IsNullOrWhiteSpace(container) ? "mp4" : container;
            if (!Regex.IsMatch(ext, @"\A[a-zA-Z0-9]{1,8}\z"))
                return BadRequest("Недопустимый формат файла.");

            var fileName = $"{typeLower}_{Guid.NewGuid():N}.{ext}";
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, fileName);

            try
            {
                await _youtubeStreamService.DownloadStreamAsync(
                    videoUrlOrId: videoUrlOrId,
                    type: typeLower,
                    qualityLabel: qualityLabel,
                    container: container,
                    saveFilePath: filePath
                );

                if (!System.IO.File.Exists(filePath))
                    return Problem("Файл не был создан.", statusCode: 500);

                Response.OnCompleted(() =>
                {
                    TryDeleteTemporaryFile(filePath);
                    return Task.CompletedTask;
                });
                return PhysicalFile(filePath, GetContentTypeByExtension(Path.GetExtension(fileName)), fileName);
            }
            catch (Exception ex)
            {
                TryDeleteTemporaryFile(filePath);
                _logger.LogError(ex, "Failed to download YouTube stream");
                return Problem("Не удалось скачать дорожку.", statusCode: 500);
            }
        }

        /// <summary>
        /// POST api/youtube/merge
        /// Ставит в очередь задачу «скачать+мердж» и возвращает taskId.
        /// </summary>
        [HttpPost("merge")]      
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]       
        public async Task<IActionResult> MergeVideoAndAudios([FromBody] MergeRequestDto? dto)
        {
            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            if (dto == null   || string.IsNullOrWhiteSpace(dto.VideoUrlOrId)     )
            {
                return BadRequest("VideoUrlOrId  обязательны.");
            }
            if (VideoId.TryParse(dto.VideoUrlOrId) is null)
                return BadRequest("Неверная ссылка или ID видео YouTube.");
            if (dto.AudioStreams?.Any(stream => stream is null ||
                    !string.Equals(stream.Type, "audio", StringComparison.OrdinalIgnoreCase)) == true)
                return BadRequest("AudioStreams должен содержать только аудиодорожки.");

            // Собираем список дорожек: сначала видео, затем все аудиодорожки
            var streams = new List<StreamDto>();

            if (!string.IsNullOrEmpty(dto.QualityLabel))
                streams.Add(
                new StreamDto
                {
                    Type = "video",
                    QualityLabel = dto.QualityLabel,
                    Container = dto.Container
                });
            
            if (dto.AudioStreams != null)
                streams.AddRange(dto.AudioStreams);

            if (streams.Count == 0)
                return BadRequest("Выберите видео или аудиодорожку.");

            try
            {
                // Теперь передаём 3 параметра: video, streams и createdBy
                var taskId = await _downloadTaskManager
                    .EnqueueDownloadAsync(dto.VideoUrlOrId, streams, userId);

                return Ok(new { TaskId = taskId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue YouTube download for user {UserId}", userId);
                return Problem("Не удалось создать задачу скачивания.", statusCode: 500);
            }
        }

        /// <summary>
        /// GET api/youtube/progress/{taskId}
        /// Возвращает прогресс по задаче.
        /// </summary>
        [HttpGet("progress/{taskId}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetProgress(string taskId)
        {
            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized();

            var task = await _downloadTaskManager.GetTaskStatusAsync(taskId, userId);
            if (task == null || task.UserId != userId)
                return NotFound();

            int progress = task.Status switch
            {
                YoutubeWorkflowStatus.Created => 0,
                YoutubeWorkflowStatus.Downloading => 50,
                YoutubeWorkflowStatus.Merging => 90,
                YoutubeWorkflowStatus.Done => 100,
                _ => 0
            };
            var hasResult = task.Status == YoutubeWorkflowStatus.Done &&
                !string.IsNullOrWhiteSpace(task.MergedFilePath);
            return Ok(new
            {
                TaskId = taskId,
                Status = task.Status.ToString(),
                Progress = progress,
                Error = task.Error,
                FileName = hasResult ? Path.GetFileName(task.MergedFilePath) : null,
                DownloadUrl = hasResult ? $"/api/youtube/downloadResult/{Uri.EscapeDataString(taskId)}" : null
            });
        }

        /// <summary>
        /// GET api/youtube/downloadResult/{taskId}
        /// Скачивает результирующий файл после слияния.
        /// </summary>
        [HttpGet("downloadResult/{taskId}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> DownloadMergedResult(string taskId)
        {
            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized();

            var task = await _downloadTaskManager.GetTaskStatusAsync(taskId, userId);
            if (task == null || task.UserId != userId)
                return NotFound();
            if (task.Status != YoutubeWorkflowStatus.Done
                || string.IsNullOrWhiteSpace(task.MergedFilePath))
            {
                return BadRequest("Задача не завершена или нет итогового файла.");
            }

            if (!System.IO.File.Exists(task.MergedFilePath))
                return NotFound("Итоговый файл не найден.");

            var name = Path.GetFileName(task.MergedFilePath);
            var contentType = GetContentTypeByExtension(Path.GetExtension(name));
            return PhysicalFile(task.MergedFilePath, contentType, name);
        }

        /// <summary>
        /// GET api/youtube/merged?createdBy=...
        /// Возвращает список всех завершённых (Done) задач, можно фильтровать по создателю.
        /// </summary>
        [HttpGet("merged")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<List<MergedVideoDto>>> GetMergedVideos()
        {
            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            try
            {
                var list = await _workflowService.GetMergedVideosAsync(userId);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list YouTube downloads for user {UserId}", userId);
                return Problem("Не удалось получить список скачиваний.", statusCode: 500);
            }
        }










        private static string GetContentTypeByExtension(string? extension)
        {
            return extension?.ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".webm" => "audio/webm",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                _ => "application/octet-stream"
            };
        }

        private static void TryDeleteTemporaryFile(string filePath)
        {
            try
            {
                System.IO.File.Delete(filePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

    }
}
