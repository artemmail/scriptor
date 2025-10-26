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
using YandexSpeech.Extensions;

namespace YourNamespace.Controllers
{
    /// <summary>
    /// DTO для запроса на склейку (merge):  
    /// - VideoUrlOrId — URL или ID видео  
    /// - CreatedBy    — идентификатор/имя пользователя, который создал задачу  
    /// - QualityLabel, Container — параметры видео  
    /// - AudioStreams — список аудиодорожек  
    /// </summary>
    public class MergeRequestDto
    {
        public string VideoUrlOrId { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
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

        public YoutubeController(
            YoutubeStreamService youtubeStreamService,
            IYoutubeDownloadTaskManager downloadTaskManager,
            YoutubeWorkflowService workflowService)
        {
            _youtubeStreamService = youtubeStreamService;
            _downloadTaskManager = downloadTaskManager;
            _workflowService = workflowService;
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

            try
            {
                var streams = await _youtubeStreamService.GetAllStreamsAsync(videoUrlOrId);
                return Ok(streams);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при получении потоков: {ex.Message}");
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

            var typeLower = type.ToLowerInvariant();
            if (typeLower != "audio" && typeLower != "video" && typeLower != "muxed")
                return BadRequest("type должен быть 'audio', 'video' или 'muxed'.");

            // формируем уникальное имя файла
            var ext = container ?? "mp4";
            var qualityPart = qualityLabel ?? "default";
            var fileName = $"{typeLower}_{qualityPart}_{Guid.NewGuid()}.{ext}";
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

                return Ok(new { Message = "Downloaded", Path = filePath });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при скачивании: {ex.Message}");
            }
        }

        /// <summary>
        /// POST api/youtube/merge
        /// Ставит в очередь задачу «скачать+мердж» и возвращает taskId.
        /// </summary>
        [HttpPost("merge")]      
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]       
        public async Task<IActionResult> MergeVideoAndAudios([FromBody] MergeRequestDto dto)
        {
            var userId = User.GetUserId();
            if (userId == null)
                return Unauthorized("User is not authenticated");

            if (dto == null   || string.IsNullOrWhiteSpace(dto.VideoUrlOrId)     )
            {
                return BadRequest("VideoUrlOrId  обязательны.");
            }

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
            
            streams.AddRange(dto.AudioStreams);

            try
            {
                // Теперь передаём 3 параметра: video, streams и createdBy
                var taskId = await _downloadTaskManager
                    .EnqueueDownloadAsync(dto.VideoUrlOrId, streams, userId);

                return Ok(new { TaskId = taskId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при постановке в очередь: {ex.Message}");
            }
        }

        /// <summary>
        /// GET api/youtube/progress/{taskId}
        /// Возвращает прогресс по задаче.
        /// </summary>
        [HttpGet("progress/{taskId}")]
        public async Task<IActionResult> GetProgress(string taskId)
        {
            var task = await _downloadTaskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound($"Задача {taskId} не найдена.");

            int progress = task.Status switch
            {
                YoutubeWorkflowStatus.Created => 0,
                YoutubeWorkflowStatus.Downloading => 50,
                YoutubeWorkflowStatus.Merging => 90,
                YoutubeWorkflowStatus.Done => 100,
                _ => 0
            };
            return Ok(new { TaskId = taskId, Status = task.Status.ToString(), Progress = progress });
        }

        /// <summary>
        /// GET api/youtube/downloadResult/{taskId}
        /// Скачивает результирующий файл после слияния.
        /// </summary>
        [HttpGet("downloadResult/{taskId}")]
        public async Task<IActionResult> DownloadMergedResult(string taskId)
        {
            var task = await _downloadTaskManager.GetTaskStatusAsync(taskId);
            if (task == null)
                return NotFound($"Задача {taskId} не найдена.");
            if (task.Status != YoutubeWorkflowStatus.Done
                || string.IsNullOrWhiteSpace(task.MergedFilePath))
            {
                return BadRequest("Задача не завершена или нет итогового файла.");
            }

            var data = await System.IO.File.ReadAllBytesAsync(task.MergedFilePath);
            var name = Path.GetFileName(task.MergedFilePath);
            var contentType = GetContentTypeByExtension(Path.GetExtension(name));
            return File(data, contentType, name);
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
                return StatusCode(500, $"Ошибка при получении списка: {ex.Message}");
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

    }
}
