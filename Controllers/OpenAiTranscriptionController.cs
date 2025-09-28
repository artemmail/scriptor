using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
        private readonly IHttpClientFactory _httpClientFactory;

        public OpenAiTranscriptionController(
            IOpenAiTranscriptionService transcriptionService,
            MyDbContext dbContext,
            IWebHostEnvironment environment,
            IServiceScopeFactory scopeFactory,
            ILogger<OpenAiTranscriptionController> logger,
            IDocumentGeneratorService documentGeneratorService,
            IHttpClientFactory httpClientFactory)
        {
            _transcriptionService = transcriptionService;
            _dbContext = dbContext;
            _environment = environment;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _documentGeneratorService = documentGeneratorService;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [RequestFormLimits(
            MultipartBodyLengthLimit = long.MaxValue,
            ValueLengthLimit = int.MaxValue,
            MultipartHeadersLengthLimit = int.MaxValue)]
        public async Task<ActionResult<OpenAiTranscriptionTaskDto>> Upload(
            [FromForm] IFormFile? file,
            [FromForm] string? fileUrl,
            [FromForm] string? clarification)
        {
            var normalizedUrl = string.IsNullOrWhiteSpace(fileUrl) ? null : fileUrl.Trim();
            var hasFile = file != null && file.Length > 0;
            var hasUrl = !string.IsNullOrEmpty(normalizedUrl);

            if (file != null && file.Length == 0)
            {
                return BadRequest("Uploaded file is empty.");
            }

            if (!hasFile && !hasUrl)
            {
                return BadRequest("Either a file or a file URL must be provided.");
            }

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var uploadsDirectory = Path.Combine(_environment.ContentRootPath, "App_Data", "transcriptions");
            Directory.CreateDirectory(uploadsDirectory);

            string storedFilePath;

            if (hasFile)
            {
                storedFilePath = await SaveUploadedFileAsync(file!, uploadsDirectory);
            }
            else
            {
                var downloadResult = await TryDownloadExternalFileAsync(normalizedUrl!, uploadsDirectory);
                if (!downloadResult.Success)
                {
                    return BadRequest(downloadResult.ErrorMessage ?? "Unable to download file from the provided URL.");
                }

                storedFilePath = downloadResult.FilePath!;
            }

            var sanitizedClarification = string.IsNullOrWhiteSpace(clarification)
                ? null
                : clarification.Trim();

            var task = await _transcriptionService.StartTranscriptionAsync(
                storedFilePath,
                userId,
                sanitizedClarification);

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
                .Select(t => new
                {
                    t.Id,
                    t.SourceFilePath,
                    t.Status,
                    t.Done,
                    t.Error,
                    t.CreatedAt,
                    t.ModifiedAt,
                    t.SegmentsTotal,
                    t.SegmentsProcessed,
                    t.Clarification
                })
                .ToListAsync();

            var result = tasks
                .Select(t => MapToDto(
                    t.Id,
                    t.SourceFilePath,
                    t.Status,
                    t.Done,
                    t.Error,
                    t.CreatedAt,
                    t.ModifiedAt,
                    t.SegmentsTotal,
                    t.SegmentsProcessed,
                    t.Clarification))
                .ToList();
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

        private static async Task<string> SaveUploadedFileAsync(IFormFile file, string uploadsDirectory)
        {
            var sanitizedName = SanitizeFileName(file.FileName);
            var storedFilePath = GenerateStoredFilePath(uploadsDirectory, sanitizedName);

            await using var stream = System.IO.File.Create(storedFilePath);
            await file.CopyToAsync(stream);

            return storedFilePath;
        }

        private async Task<(bool Success, string? FilePath, string? ErrorMessage)> TryDownloadExternalFileAsync(
            string fileUrl,
            string uploadsDirectory)
        {
            if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
            {
                return (false, null, "Invalid file URL provided.");
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, "Only HTTP and HTTPS URLs are supported.");
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10);

                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Failed to download transcription source file from {Url}. Status code: {StatusCode}",
                        uri,
                        response.StatusCode);
                    return (false, null, "Unable to download file from the provided URL.");
                }

                var remoteFileName = ResolveRemoteFileName(response, uri);
                var sanitizedName = SanitizeFileName(remoteFileName);
                var storedFilePath = GenerateStoredFilePath(uploadsDirectory, sanitizedName);

                await using var fileStream = System.IO.File.Create(storedFilePath);
                await response.Content.CopyToAsync(fileStream);

                return (true, storedFilePath, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download transcription source file from {Url}", fileUrl);
                return (false, null, "Unable to download file from the provided URL.");
            }
        }

        private static string ResolveRemoteFileName(HttpResponseMessage response, Uri uri)
        {
            var contentDisposition = response.Content.Headers.ContentDisposition;
            var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName.Trim('"');
            }

            var queryFileName = TryExtractFileNameFromQuery(uri);
            if (!string.IsNullOrWhiteSpace(queryFileName))
            {
                return queryFileName;
            }

            var pathFileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(pathFileName))
            {
                return pathFileName;
            }

            var extension = TryGetExtensionFromContentType(response.Content.Headers.ContentType?.MediaType);
            return string.IsNullOrEmpty(extension)
                ? "external-file"
                : $"external-file{extension}";
        }

        private static string? TryExtractFileNameFromQuery(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.Query))
            {
                return null;
            }

            var queryValues = QueryHelpers.ParseQuery(uri.Query);
            foreach (var key in new[] { "filename", "file", "name", "download" })
            {
                if (queryValues.TryGetValue(key, out var values))
                {
                    var candidate = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static string? TryGetExtensionFromContentType(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return null;
            }

            return mediaType.ToLowerInvariant() switch
            {
                "audio/mpeg" => ".mp3",
                "audio/mp3" => ".mp3",
                "audio/wav" => ".wav",
                "audio/x-wav" => ".wav",
                "audio/webm" => ".webm",
                "audio/ogg" => ".ogg",
                "audio/aac" => ".aac",
                "audio/x-m4a" => ".m4a",
                "audio/flac" => ".flac",
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "video/quicktime" => ".mov",
                "video/x-msvideo" => ".avi",
                "video/mpeg" => ".mpeg",
                _ => null,
            };
        }

        private static string GenerateStoredFilePath(string uploadsDirectory, string sanitizedName)
        {
            var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}__{sanitizedName}";
            return Path.Combine(uploadsDirectory, storedFileName);
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
            return MapToDto(
                task.Id,
                task.SourceFilePath,
                task.Status,
                task.Done,
                task.Error,
                task.CreatedAt,
                task.ModifiedAt,
                task.SegmentsTotal,
                task.SegmentsProcessed,
                task.Clarification);
        }

        private static OpenAiTranscriptionTaskDto MapToDto(
            string id,
            string sourceFilePath,
            OpenAiTranscriptionStatus status,
            bool done,
            string? error,
            DateTime createdAt,
            DateTime modifiedAt,
            int segmentsTotal,
            int segmentsProcessed,
            string? clarification)
        {
            return new OpenAiTranscriptionTaskDto
            {
                Id = id,
                FileName = Path.GetFileName(sourceFilePath) ?? sourceFilePath,
                DisplayName = ResolveDisplayName(sourceFilePath),
                Status = status,
                Done = done,
                Error = error,
                CreatedAt = createdAt,
                ModifiedAt = modifiedAt,
                SegmentsTotal = segmentsTotal,
                SegmentsProcessed = segmentsProcessed,
                Clarification = clarification
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
                MarkdownText = task.MarkdownText,
                Clarification = task.Clarification
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
