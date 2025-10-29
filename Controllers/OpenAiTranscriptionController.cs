using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using YandexSpeech.Extensions;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Whisper;

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
        private readonly IYandexDiskDownloadService _yandexDiskDownloadService;
        private readonly ISubscriptionAccessService _subscriptionAccessService;

        public OpenAiTranscriptionController(
            IOpenAiTranscriptionService transcriptionService,
            MyDbContext dbContext,
            IWebHostEnvironment environment,
            IServiceScopeFactory scopeFactory,
            ILogger<OpenAiTranscriptionController> logger,
            IDocumentGeneratorService documentGeneratorService,
            IHttpClientFactory httpClientFactory,
            IYandexDiskDownloadService yandexDiskDownloadService,
            ISubscriptionAccessService subscriptionAccessService)
        {
            _transcriptionService = transcriptionService;
            _dbContext = dbContext;
            _environment = environment;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _documentGeneratorService = documentGeneratorService;
            _httpClientFactory = httpClientFactory;
            _yandexDiskDownloadService = yandexDiskDownloadService;
            _subscriptionAccessService = subscriptionAccessService ?? throw new ArgumentNullException(nameof(subscriptionAccessService));
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
            [FromForm] string? clarification,
            [FromForm] int? recognitionProfileId,
            CancellationToken cancellationToken)
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

            if (hasFile && file!.Length <= OpenAiTranscriptionFileHelper.HtmlDetectionMaxFileSize)
            {
                await using var detectionStream = file.OpenReadStream();
                if (await HtmlFileDetector
                        .IsHtmlAsync(detectionStream, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return BadRequest(OpenAiTranscriptionFileHelper.HtmlFileNotSupportedMessage);
                }
            }

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email);

            //  var userEmail = User.FindFirstValue(ClaimTypes.Email);

            var authorization = await _subscriptionAccessService
                .AuthorizeTranscriptionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            if (!authorization.IsAllowed)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new UsageLimitExceededResponse
                {
                    Message = authorization.Message ?? "Превышен месячный лимит транскрибаций.",
                    PaymentUrl = authorization.PaymentUrl ?? "/billing",
                    RemainingQuota = authorization.RemainingQuota,
                    RecognizedTitles = authorization.RecognizedTitles
                });
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
                var validationResult = await ValidateExternalFileUrlAsync(normalizedUrl!);
                if (!validationResult.Success)
                {
                    return BadRequest(validationResult.ErrorMessage ?? "Unable to download file from the provided URL.");
                }

                var sanitizedName = OpenAiTranscriptionFileHelper.SanitizeFileName(validationResult.FileName);
                storedFilePath = OpenAiTranscriptionFileHelper.GenerateStoredFilePath(uploadsDirectory, sanitizedName);
            }

            var sanitizedClarification = string.IsNullOrWhiteSpace(clarification)
                ? null
                : clarification.Trim();

            var profileQuery = _dbContext.RecognitionProfiles
                .AsNoTracking()
                .AsQueryable();

            RecognitionProfile? profile;

            if (recognitionProfileId.HasValue)
            {
                profile = await profileQuery
                    .FirstOrDefaultAsync(p => p.Id == recognitionProfileId.Value);

                if (profile == null)
                {
                    return BadRequest("Указанный профиль распознавания не найден.");
                }
            }
            else
            {
                profile = await profileQuery
                    .FirstOrDefaultAsync(p => p.Name == RecognitionProfileNames.PunctuationOnly);

                if (profile == null)
                {
                    return BadRequest("Профиль распознавания по умолчанию не найден.");
                }
            }

            var task = await _transcriptionService.StartTranscriptionAsync(
                storedFilePath,
                userId,
                sanitizedClarification,
                hasUrl ? normalizedUrl : null,
                profile.Id,
                profile.DisplayedName);

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

            var dto = MapToDto(
                task.Id,
                task.SourceFilePath,
                task.Status,
                task.Done,
                task.Error,
                task.CreatedAt,
                task.ModifiedAt,
                task.SegmentsTotal,
                task.SegmentsProcessed,
                task.Clarification,
                task.RecognitionProfileId,
                profile.Name,
                task.RecognitionProfileDisplayedName ?? profile.DisplayedName,
                userEmail);

            dto.RemainingMonthlyQuota = authorization.RemainingQuota;

            return CreatedAtAction(
                nameof(GetById),
                new { id = task.Id },
                dto);
        }

        [HttpGet]
        public async Task<ActionResult> List([FromQuery] bool includeAll = false)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin");
            var canViewAll = includeAll && isAdmin;

            var baseQuery = _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .Include(t => t.RecognitionProfile)
                .AsQueryable();

            if (!canViewAll)
            {
                baseQuery = baseQuery.Where(t => t.CreatedBy == userId);
            }

            var tasks = await (
                from task in baseQuery
                join user in _dbContext.Users.AsNoTracking()
                    on task.CreatedBy equals user.Id into users
                from creator in users.DefaultIfEmpty()
                orderby task.ModifiedAt descending, task.CreatedAt descending
                select new
                {
                    task.Id,
                    task.SourceFilePath,
                    task.Status,
                    task.Done,
                    task.Error,
                    task.CreatedAt,
                    task.ModifiedAt,
                    task.SegmentsTotal,
                    task.SegmentsProcessed,
                    task.Clarification,
                    task.RecognitionProfileId,
                    ProfileName = task.RecognitionProfile != null ? task.RecognitionProfile.Name : null,
                    ProfileDisplayedName = task.RecognitionProfileDisplayedName
                        ?? (task.RecognitionProfile != null ? task.RecognitionProfile.DisplayedName : null),
                    CreatedByEmail = creator.Email
                }
            ).ToListAsync();

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
                    t.Clarification,
                    t.RecognitionProfileId,
                    t.ProfileName,
                    t.ProfileDisplayedName,
                    isAdmin ? t.CreatedByEmail : null))
                .ToList();
            return Ok(result);
        }

        [HttpGet("recognition-profiles")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<OpenAiRecognitionProfileOptionDto>>> GetRecognitionProfiles()
        {
            var profiles = await _dbContext.RecognitionProfiles
                .AsNoTracking()
                .OrderBy(p => p.DisplayedName)
                .Select(p => new OpenAiRecognitionProfileOptionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    DisplayedName = p.DisplayedName,
                    ClarificationTemplate = p.ClarificationTemplate,
                    Hint = p.Hint
                })
                .ToListAsync();

            return Ok(profiles);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OpenAiTranscriptionTaskDetailsDto>> GetById(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin");
            var task = await LoadTaskWithDetailsAsync(id, userId, isAdmin);

            if (task == null)
            {
                return NotFound();
            }

            string? createdByEmail = null;
            if (isAdmin)
            {
                createdByEmail = await GetUserEmailAsync(task.CreatedBy);
            }

            return Ok(MapToDetailsDto(task, createdByEmail));
        }

        [HttpPost("{id}/continue")]
        public async Task<ActionResult<OpenAiTranscriptionTaskDetailsDto>> Continue(string id)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var isAdmin = User.IsInRole("Admin");

            var taskExists = await _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .AnyAsync(t => t.Id == id && (t.CreatedBy == userId || isAdmin));

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

            string? createdByEmail = null;
            if (isAdmin)
            {
                createdByEmail = await GetUserEmailAsync(preparedTask.CreatedBy);
            }

            return Ok(MapToDetailsDto(preparedTask, createdByEmail));
        }

        [HttpPost("{id}/analytics")]
        public async Task<ActionResult<OpenAiTranscriptionTaskDto>> CloneForAnalytics(
            string id,
            [FromBody] StartAnalyticsRequest? request)
        {
            if (request == null || !request.RecognitionProfileId.HasValue)
            {
                return BadRequest("Не выбран профиль распознавания.");
            }

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var userEmail = User.FindFirstValue(ClaimTypes.Email);

            try
            {
                var newTask = await _transcriptionService.CloneForPostProcessingAsync(
                    id,
                    userId,
                    request.RecognitionProfileId.Value,
                    request.Clarification);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var scopedService = scope.ServiceProvider.GetRequiredService<IOpenAiTranscriptionService>();
                        await scopedService.ContinueTranscriptionAsync(newTask.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to run analytics for OpenAI transcription task {TaskId}", newTask.Id);
                    }
                });

                return Ok(MapToDto(newTask, userEmail));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
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

        [HttpGet("{id}/export/srt")]
        [Produces("application/x-subrip")]
        public async Task<IActionResult> ExportSrt(string id)
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

            if (string.IsNullOrWhiteSpace(task.SegmentsJson))
            {
                return BadRequest("Task does not contain transcription segments.");
            }

            WhisperTranscriptionResponse? parsed;

            try
            {
                parsed = WhisperTranscriptionHelper.Parse(task.SegmentsJson);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse transcription segments for task {TaskId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to parse transcription segments.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while parsing transcription segments for task {TaskId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to parse transcription segments.");
            }

            if (parsed == null || parsed.Segments == null || parsed.Segments.Count == 0)
            {
                return BadRequest("Task does not contain transcription segments.");
            }

            var entries = BuildSrtEntriesFromWhisper(parsed);
            var srtContent = SrtFormatter.Build(entries);

            if (string.IsNullOrWhiteSpace(srtContent))
            {
                return BadRequest("Task does not contain transcription segments.");
            }

            var fileName = CreateExportFileName(task, "srt");
            var encoding = new UTF8Encoding(false);
            var bytes = encoding.GetBytes(srtContent);

            return File(bytes, "application/x-subrip", fileName);
        }

        [HttpGet("{id}/export/pdf")]
        public async Task<IActionResult> ExportPdf(string id)
        {
            var taskResult = await LoadTaskForExportAsync(id);
            if (taskResult.Result != null)
            {
                return taskResult.Result;
            }

            var (entity, markdown) = taskResult.Value;
            var filePath = await _documentGeneratorService.GeneratePdfFromMarkdownAsync(entity.Id, markdown);
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

            var (entity, markdown) = taskResult.Value;
            var filePath = await _documentGeneratorService.GenerateWordFromMarkdownAsync(entity.Id, markdown);
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

            var (entity, markdown) = taskResult.Value;
            var bbcode = await _documentGeneratorService.GenerateBbcodeFromMarkdownAsync(entity.Id, markdown);
            var fileName = CreateExportFileName(entity, "bbcode");
            var bytes = Encoding.UTF8.GetBytes(bbcode);
            return File(bytes, "text/plain", fileName);
        }

        private async Task<OpenAiTranscriptionTask?> LoadTaskWithDetailsAsync(string taskId, string createdBy, bool isAdmin)
        {
            var query = _dbContext.OpenAiTranscriptionTasks
                .AsNoTracking()
                .Include(t => t.RecognitionProfile)
                .Where(t => t.Id == taskId);

            if (!isAdmin)
            {
                query = query.Where(t => t.CreatedBy == createdBy);
            }

            var task = await query.FirstOrDefaultAsync();

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

        private async Task<string?> GetUserEmailAsync(string userId)
        {
            return await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
        }

        private static async Task<string> SaveUploadedFileAsync(IFormFile file, string uploadsDirectory)
        {
            var sanitizedName = OpenAiTranscriptionFileHelper.SanitizeFileName(file.FileName);
            var storedFilePath = OpenAiTranscriptionFileHelper.GenerateStoredFilePath(uploadsDirectory, sanitizedName);

            await using var stream = System.IO.File.Create(storedFilePath);
            await file.CopyToAsync(stream);

            return storedFilePath;
        }

        private async Task<(bool Success, string? FileName, string? ErrorMessage)> ValidateExternalFileUrlAsync(string fileUrl)
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

            if (_yandexDiskDownloadService.IsYandexDiskUrl(uri))
            {
                var fileName = TryExtractFileNameFromQuery(uri) ?? Path.GetFileName(uri.LocalPath);
                return (true, fileName, null);
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
                using var headResponse = await httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);

                if (headResponse.IsSuccessStatusCode)
                {
                    var fileName = ResolveRemoteFileName(headResponse, uri);
                    return (true, fileName, null);
                }

                if (headResponse.StatusCode != HttpStatusCode.MethodNotAllowed)
                {
                    _logger.LogWarning(
                        "Failed to validate transcription source file at {Url}. Status code: {StatusCode}",
                        uri,
                        headResponse.StatusCode);
                    return (false, null, "Unable to access file at the provided URL.");
                }

                using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Failed to validate transcription source file at {Url}. Status code: {StatusCode}",
                        uri,
                        response.StatusCode);
                    return (false, null, "Unable to access file at the provided URL.");
                }

                var remoteFileName = ResolveRemoteFileName(response, uri);
                return (true, remoteFileName, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate transcription source file at {Url}", fileUrl);
                return (false, null, "Unable to access file at the provided URL.");
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

        private static OpenAiTranscriptionTaskDto MapToDto(OpenAiTranscriptionTask task, string? createdByEmail = null)
        {
            var recognitionProfileName = task.RecognitionProfile?.Name;
            var recognitionProfileDisplayedName = task.RecognitionProfileDisplayedName
                ?? task.RecognitionProfile?.DisplayedName;

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
                task.Clarification,
                task.RecognitionProfileId,
                recognitionProfileName,
                recognitionProfileDisplayedName,
                createdByEmail);
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
            string? clarification,
            int? recognitionProfileId,
            string? recognitionProfileName,
            string? recognitionProfileDisplayedName,
            string? createdByEmail)
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
                Clarification = clarification,
                RecognitionProfileId = recognitionProfileId,
                RecognitionProfileName = recognitionProfileName,
                RecognitionProfileDisplayedName = recognitionProfileDisplayedName,
                CreatedByEmail = createdByEmail
            };
        }

        private static OpenAiTranscriptionTaskDetailsDto MapToDetailsDto(OpenAiTranscriptionTask task, string? createdByEmail = null)
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
                HasSegments = !string.IsNullOrWhiteSpace(task.SegmentsJson),
                Clarification = task.Clarification,
                RecognitionProfileId = task.RecognitionProfileId,
                RecognitionProfileName = task.RecognitionProfile?.Name,
                RecognitionProfileDisplayedName = task.RecognitionProfileDisplayedName
                    ?? task.RecognitionProfile?.DisplayedName,
                CreatedByEmail = createdByEmail
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
            var sanitized = OpenAiTranscriptionFileHelper.SanitizeFileName(fileName);
            if (!sanitized.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
            {
                sanitized = $"{sanitized}.{extension}";
            }

            return sanitized;
        }

        private async Task<ActionResult<(OpenAiTranscriptionTask Task, string Markdown)>> LoadTaskForExportAsync(string id)
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

            var markdown = ResolveMarkdownContent(task);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return BadRequest("Task does not contain formatted Markdown.");
            }

            return (task, markdown);
        }

        private static string? ResolveMarkdownContent(OpenAiTranscriptionTask task)
        {
            if (!string.IsNullOrWhiteSpace(task.MarkdownText))
            {
                return task.MarkdownText;
            }

            var fallback = !string.IsNullOrWhiteSpace(task.ProcessedText)
                ? task.ProcessedText
                : task.RecognizedText;

            if (string.IsNullOrWhiteSpace(fallback))
            {
                return null;
            }

            return NormalizePlainTextForMarkdown(fallback);
        }

        private static string NormalizePlainTextForMarkdown(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var builder = new StringBuilder();
            var previousEmpty = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();

                if (trimmed.Length == 0)
                {
                    if (!previousEmpty)
                    {
                        builder.AppendLine();
                    }

                    previousEmpty = true;
                }
                else
                {
                    builder.AppendLine(trimmed);
                    previousEmpty = false;
                }
            }

            return builder.ToString().TrimEnd('\r', '\n');
        }

        public class StartAnalyticsRequest
        {
            public int? RecognitionProfileId { get; set; }

            public string? Clarification { get; set; }
        }

        public class UpdateMarkdownRequest
        {
            public string Markdown { get; set; } = string.Empty;
        }

        private static List<SrtFormatter.SrtEntry> BuildSrtEntriesFromWhisper(WhisperTranscriptionResponse parsed)
        {
            var result = new List<SrtFormatter.SrtEntry>();

            foreach (var segment in parsed.Segments)
            {
                if (segment == null)
                {
                    continue;
                }

                var start = ToSafeTimeSpan(segment.Start);
                TimeSpan? end = null;

                if (!double.IsNaN(segment.End) && !double.IsInfinity(segment.End))
                {
                    var endValue = ToSafeTimeSpan(segment.End);
                    if (endValue > start)
                    {
                        end = endValue;
                    }
                }

                var text = (segment.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text) && segment.Words != null && segment.Words.Count > 0)
                {
                    text = string.Concat(segment.Words.Select(word => word.Word ?? string.Empty)).Trim();
                }

                result.Add(new SrtFormatter.SrtEntry
                {
                    Start = start,
                    End = end,
                    Text = text
                });
            }

            return result;
        }

        private static TimeSpan ToSafeTimeSpan(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return TimeSpan.Zero;
            }

            if (seconds < 0)
            {
                seconds = 0;
            }

            return TimeSpan.FromSeconds(seconds);
        }
    }
}
