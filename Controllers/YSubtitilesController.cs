using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
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

        public YSubtitilesController(
            ICaptionTaskManager taskManager,
            IYoutubeCaptionService youtubeCaptionService,
            IDocumentGeneratorService pdfGeneratorService,
            IYSubtitlesService ySubtitlesService,
            YoutubeWorkflowService workflow, // <-- добавили
            ISubscriptionAccessService subscriptionAccessService
        )
        {
            _taskManager = taskManager;
            _youtubeCaptionService = youtubeCaptionService;
            _pdfGeneratorService = pdfGeneratorService;
            _ySubtitlesService = ySubtitlesService;
            _workflow = workflow; // <-- добавили
            _subscriptionAccessService = subscriptionAccessService ?? throw new ArgumentNullException(nameof(subscriptionAccessService));
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
            [FromQuery] string userId = null)
        {
            var (items, totalCount) = await _ySubtitlesService.GetTasksPagedAsync(
                page, pageSize, sortField, sortOrder, filter, userId);

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
        [Produces("text/plain")]
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
    }
}
