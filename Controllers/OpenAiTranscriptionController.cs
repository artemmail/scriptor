using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.Extensions;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OpenAiTranscriptionController : ControllerBase
    {
        private readonly IOpenAiTranscriptionService _transcriptionService;
        private readonly MyDbContext _dbContext;
        private readonly IWebHostEnvironment _environment;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OpenAiTranscriptionController> _logger;
        private readonly IDocumentGeneratorService _documentGeneratorService;

        public OpenAiTranscriptionController(
            IOpenAiTranscriptionService transcriptionService,
            MyDbContext dbContext,
            IWebHostEnvironment environment,
            IServiceScopeFactory scopeFactory,
            ILogger<OpenAiTranscriptionController> logger,
            IDocumentGeneratorService documentGeneratorService)
        {
            _transcriptionService = transcriptionService;
            _dbContext = dbContext;
            _environment = environment;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _documentGeneratorService = documentGeneratorService;
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(
            MultipartBodyLengthLimit = long.MaxValue,
            ValueLengthLimit = int.MaxValue,
            MultipartHeadersLengthLimit = int.MaxValue)]
        public async Task<ActionResult<OpenAiTranscriptionTaskDto>> Upload([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File not provided.");
            }

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var uploadsDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "transcriptions");
            Directory.CreateDirectory(uploadsDirectory);

            var sanitizedName = SanitizeFileName(file.FileName);
            var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}__{sanitizedName}";
            var storedFilePath = Path.Combine(uploadsDirectory, storedFileName);

            await using (var stream = System.IO.File.Create(storedFilePath))
            {
                await file.CopyToAsync(stream);
            }

            var task = await _transcriptionService.StartTranscriptionAsync(storedFilePath, userId);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedService = scope.ServiceProvider.GetRequiredService<IOpenAiTranscriptionService>();
                    await scopedService.ContinueTranscriptionAsync(task.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to continue OpenAI transcription task {TaskId}", task.Id);
                }
            });

            return CreatedAtAction(nameof(GetById), new { id = task.Id }, MapToDto(task));
        }

        [HttpGet]
        public async Task<ActionResult> List()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var tasks = await _dbContext.OpenAiTranscriptionTasks
                .Where(t => t.CreatedBy == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var result = tasks.Select(MapToDto).ToList();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OpenAiTranscriptionTaskDetailsDto>> GetById(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var task = await LoadTaskWithDetailsAsync(id, userId);

            if (task == null)
            {
                return NotFound();
            }

            return Ok(MapToDetailsDto(task));
        }

        [HttpPost("{id}/continue")]
        public async Task<ActionResult<OpenAiTranscriptionTaskDetailsDto>> Continue(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var taskExists = await _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .AnyAsync(t => t.Id == id && t.CreatedBy == userId);

            if (!taskExists)
            {
                return NotFound();
            }

            var preparedTask = await _transcriptionService.PrepareForContinuationAsync(id);
            if (preparedTask == null)
            {
                return NotFound();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedService = scope.ServiceProvider.GetRequiredService<IOpenAiTranscriptionService>();
                    await scopedService.ContinueTranscriptionAsync(id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to continue OpenAI transcription task {TaskId}", id);
                }
            });

            return Ok(MapToDetailsDto(preparedTask));
        }

        [HttpPut("{id}/markdown")]
        public async Task<IActionResult> UpdateMarkdown(string id, [FromBody] UpdateMarkdownRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Markdown))
            {
                return BadRequest("Markdown must be provided.");
            }

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var task = await _dbContext.OpenAiTranscriptionTasks
                .FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == userId);

            if (task == null)
            {
                return NotFound();
            }

            task.MarkdownText = request.Markdown;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var task = await _dbContext.OpenAiTranscriptionTasks
                .Include(t => t.Segments)
                .Include(t => t.Steps)
                .FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == userId);

            if (task == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrEmpty(task.SourceFilePath))
            {
                TryDeleteFile(task.SourceFilePath);
            }

            if (!string.IsNullOrEmpty(task.ConvertedFilePath))
            {
                TryDeleteFile(task.ConvertedFilePath);
            }

            if (task.Segments?.Any() == true)
            {
                _dbContext.OpenAiRecognizedSegments.RemoveRange(task.Segments);
            }

            if (task.Steps?.Any() == true)
            {
                _dbContext.OpenAiTranscriptionSteps.RemoveRange(task.Steps);
            }

            _dbContext.OpenAiTranscriptionTasks.Remove(task);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("{id}/export/pdf")]
        public async Task<IActionResult> ExportPdf(string id)
        {
            var taskResult = await LoadTaskForExportAsync(id);
            if (taskResult.Result != null)
            {
                return taskResult.Result;
            }

            var entity = taskResult.Value!;
            var filePath = await _documentGeneratorService.GeneratePdfFromMarkdownAsync(entity.Id, entity.MarkdownText!);
            try
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var fileName = CreateExportFileName(entity, "pdf");
                return File(bytes, "application/pdf", fileName);
            }
            finally
            {
                TryDeleteFile(filePath);
            }
        }

        [HttpGet("{id}/export/docx")]
        public async Task<IActionResult> ExportDocx(string id)
        {
            var taskResult = await LoadTaskForExportAsync(id);
            if (taskResult.Result != null)
            {
                return taskResult.Result;
            }

            var entity = taskResult.Value!;
            var filePath = await _documentGeneratorService.GenerateWordFromMarkdownAsync(entity.Id, entity.MarkdownText!);
            try
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var fileName = CreateExportFileName(entity, "docx");
                return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
            }
            finally
            {
                TryDeleteFile(filePath);
            }
        }

        [HttpGet("{id}/export/bbcode")]
        public async Task<IActionResult> ExportBbcode(string id)
        {
            var taskResult = await LoadTaskForExportAsync(id);
            if (taskResult.Result != null)
            {
                return taskResult.Result;
            }

            var entity = taskResult.Value!;
            var bbcode = await _documentGeneratorService.GenerateBbcodeFromMarkdownAsync(entity.Id, entity.MarkdownText!);
            var fileName = CreateExportFileName(entity, "bbcode");
            var bytes = Encoding.UTF8.GetBytes(bbcode);
            return File(bytes, "text/plain", fileName);
        }

        private async Task<OpenAiTranscriptionTask?> LoadTaskWithDetailsAsync(string taskId, string createdBy)
        {
            var task = await _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.CreatedBy == createdBy);

            if (task == null)
            {
                return null;
            }

            var steps = await _dbContext.OpenAiTranscriptionSteps
                .AsNoTracking()
                .Where(s => s.TaskId == task.Id)
                .OrderBy(s => s.StartedAt)
                .ThenBy(s => s.Id)
                .ToListAsync();

            var segments = await _dbContext.OpenAiRecognizedSegments
                .AsNoTracking()
                .Where(s => s.TaskId == task.Id)
                .OrderBy(s => s.Order)
                .ToListAsync();

            task.Steps = steps;
            task.Segments = segments;

            return task;
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string((fileName ?? string.Empty).Where(c => !invalidChars.Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "audio";
            }

            var extension = Path.GetExtension(cleaned);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(cleaned);

            if (nameWithoutExtension.Length > 80)
            {
                nameWithoutExtension = nameWithoutExtension[..80];
            }

            return string.IsNullOrEmpty(extension)
                ? nameWithoutExtension
                : $"{nameWithoutExtension}{extension}";
        }

        private static string ResolveDisplayName(string storedPath)
        {
            var fileName = Path.GetFileName(storedPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return "Unknown file";
            }

            var separatorIndex = fileName.IndexOf("__", StringComparison.Ordinal);
            if (separatorIndex >= 0 && separatorIndex + 2 < fileName.Length)
            {
                return fileName[(separatorIndex + 2)..];
            }

            return fileName;
        }

        private static OpenAiTranscriptionTaskDto MapToDto(OpenAiTranscriptionTask task)
        {
            return new OpenAiTranscriptionTaskDto
            {
                Id = task.Id,
                FileName = Path.GetFileName(task.SourceFilePath) ?? task.SourceFilePath,
                DisplayName = ResolveDisplayName(task.SourceFilePath),
                Status = task.Status,
                Done = task.Done,
                Error = task.Error,
                CreatedAt = task.CreatedAt,
                ModifiedAt = task.ModifiedAt,
                SegmentsTotal = task.SegmentsTotal,
                SegmentsProcessed = task.SegmentsProcessed
            };
        }

        private static OpenAiTranscriptionTaskDetailsDto MapToDetailsDto(OpenAiTranscriptionTask task)
        {
            var dto = new OpenAiTranscriptionTaskDetailsDto
            {
                Id = task.Id,
                FileName = Path.GetFileName(task.SourceFilePath) ?? task.SourceFilePath,
                DisplayName = ResolveDisplayName(task.SourceFilePath),
                Status = task.Status,
                Done = task.Done,
                Error = task.Error,
                CreatedAt = task.CreatedAt,
                ModifiedAt = task.ModifiedAt,
                RecognizedText = task.RecognizedText,
                ProcessedText = task.ProcessedText,
                MarkdownText = task.MarkdownText
            };

            dto.Steps = task.Steps?
                .OrderBy(s => s.StartedAt)
                .ThenBy(s => s.Id)
                .Select(step => new OpenAiTranscriptionStepDto
                {
                    Id = step.Id,
                    Step = step.Step,
                    Status = step.Status,
                    StartedAt = step.StartedAt,
                    FinishedAt = step.FinishedAt,
                    Error = step.Error
                })
                .ToList() ?? new List<OpenAiTranscriptionStepDto>();

            dto.Segments = task.Segments?
                .OrderBy(s => s.Order)
                .Select(segment => new OpenAiRecognizedSegmentDto
                {
                    SegmentId = segment.SegmentId,
                    Order = segment.Order,
                    Text = segment.Text,
                    ProcessedText = segment.ProcessedText,
                    IsProcessed = segment.IsProcessed,
                    IsProcessing = segment.IsProcessing,
                    StartSeconds = segment.StartSeconds,
                    EndSeconds = segment.EndSeconds
                })
                .ToList() ?? new List<OpenAiRecognizedSegmentDto>();

            return dto;
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {FilePath} for transcription task", path);
            }
        }

        private string CreateExportFileName(OpenAiTranscriptionTask task, string extension)
        {
            var displayName = ResolveDisplayName(task.SourceFilePath);
            var baseName = Path.GetFileNameWithoutExtension(displayName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "transcription";
            }

            var fileName = $"{baseName}.{extension}";
            var sanitized = SanitizeFileName(fileName);
            if (!sanitized.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = $"{sanitized}.{extension}";
            }

            return sanitized;
        }

        private async Task<ActionResult<OpenAiTranscriptionTask>> LoadTaskForExportAsync(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var task = await _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == userId);

            if (task == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(task.MarkdownText))
            {
                return BadRequest("Task does not contain formatted Markdown.");
            }

            return task;
        }

        public class UpdateMarkdownRequest
        {
            public string Markdown { get; set; } = string.Empty;
        }
    }
}
