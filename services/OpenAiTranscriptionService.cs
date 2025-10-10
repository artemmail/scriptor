using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Whisper;

namespace YandexSpeech.services
{
    public class OpenAiTranscriptionService : IOpenAiTranscriptionService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<OpenAiTranscriptionService> _logger;
        private readonly string _openAiApiKey;
        private readonly string _workingDirectory;
        private const string FormattingModel = "gpt-4.1-mini";
        private readonly IWhisperTranscriptionService _whisperTranscriptionService;
        private readonly IPunctuationService _punctuationService;
        private const int FormattingMaxAttempts = 5;
        private static readonly TimeSpan FormattingRetryDelay = TimeSpan.FromSeconds(5);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IYandexDiskDownloadService _yandexDiskDownloadService;
        private readonly IFfmpegService _ffmpegService;

        protected virtual string SegmentProcessingProfileName => RecognitionProfileNames.PunctuationOnly;

        public OpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILogger<OpenAiTranscriptionService> logger,
            IPunctuationService punctuationService,
            IWhisperTranscriptionService whisperTranscriptionService,
            IHttpClientFactory httpClientFactory,
            IYandexDiskDownloadService yandexDiskDownloadService,
            IFfmpegService ffmpegService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _punctuationService = punctuationService;
            _whisperTranscriptionService = whisperTranscriptionService;
            _httpClientFactory = httpClientFactory;
            _yandexDiskDownloadService = yandexDiskDownloadService;
            _ffmpegService = ffmpegService;
            _openAiApiKey = configuration["OpenAI:ApiKey"]
                             ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            _workingDirectory = Path.Combine(Path.GetTempPath(), "openai-transcriptions");
            Directory.CreateDirectory(_workingDirectory);
        }

        public async Task<OpenAiTranscriptionTask> StartTranscriptionAsync(
            string sourceFilePath,
            string createdBy,
            string? clarification = null,
            string? sourceFileUrl = null,
            int? recognitionProfileId = null,
            string? recognitionProfileDisplayedName = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path must be provided.", nameof(sourceFilePath));

            if (string.IsNullOrWhiteSpace(sourceFileUrl) && !File.Exists(sourceFilePath))
                throw new FileNotFoundException("Source file not found.", sourceFilePath);

            if (string.IsNullOrWhiteSpace(createdBy))
                throw new ArgumentException("Creator identifier must be provided.", nameof(createdBy));

            var task = new OpenAiTranscriptionTask
            {
                SourceFilePath = sourceFilePath,
                SourceFileUrl = sourceFileUrl,
                CreatedBy = createdBy,
                Clarification = string.IsNullOrWhiteSpace(clarification)
                    ? null
                    : clarification.Trim(),
                Status = string.IsNullOrWhiteSpace(sourceFileUrl)
                    ? OpenAiTranscriptionStatus.Created
                    : OpenAiTranscriptionStatus.Downloading,
                Done = false,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                RecognitionProfileId = recognitionProfileId,
                RecognitionProfileDisplayedName = string.IsNullOrWhiteSpace(recognitionProfileDisplayedName)
                    ? null
                    : recognitionProfileDisplayedName.Trim()
            };

            _dbContext.OpenAiTranscriptionTasks.Add(task);
            await _dbContext.SaveChangesAsync();

            return task;
        }

        public async Task<OpenAiTranscriptionTask?> PrepareForContinuationAsync(string taskId)
        {
            var task = await _dbContext.OpenAiTranscriptionTasks
                .Include(t => t.Steps)
                .Include(t => t.Segments)
                .Include(t => t.RecognitionProfile)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return null;

            await PrepareTaskForContinuationAsync(task);
            return task;
        }

        public async Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId)
        {
            var task = await _dbContext.OpenAiTranscriptionTasks
                .Include(t => t.Steps)
                .Include(t => t.RecognitionProfile)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return null;

            await PrepareTaskForContinuationAsync(task);

            var activeStatuses = new HashSet<OpenAiTranscriptionStatus>
            {
                OpenAiTranscriptionStatus.Downloading,
                OpenAiTranscriptionStatus.Created,
                OpenAiTranscriptionStatus.Converting,
                OpenAiTranscriptionStatus.Transcribing,
                OpenAiTranscriptionStatus.Segmenting,
                OpenAiTranscriptionStatus.ProcessingSegments
               
            };

            try
            {
                while (!task.Done
                       && task.Status != OpenAiTranscriptionStatus.Error
                       && activeStatuses.Contains(task.Status))
                {
                    var statusBefore = task.Status;
                    var processedBefore = task.SegmentsProcessed;
                    var modifiedBefore = task.ModifiedAt;
                    var doneBefore = task.Done;

                    switch (task.Status)
                    {
                        case OpenAiTranscriptionStatus.Downloading:
                            await RunDownloadStepAsync(task);
                            break;
                        case OpenAiTranscriptionStatus.Created:
                        case OpenAiTranscriptionStatus.Converting:
                            await RunConversionStepAsync(task);
                            break;
                        case OpenAiTranscriptionStatus.Transcribing:
                            await RunTranscriptionStepAsync(task);
                            break;
                        case OpenAiTranscriptionStatus.Segmenting:
                            await RunSegmentingStepAsync(task);
                            break;
                        case OpenAiTranscriptionStatus.ProcessingSegments:
                            await RunSegmentProcessingStepAsync(task);
                            break;                       
                    }

                    if (task.Status == statusBefore
                        && task.SegmentsProcessed == processedBefore
                        && task.ModifiedAt == modifiedBefore
                        && task.Done == doneBefore)
                    {
                        _logger.LogWarning(
                            "No progress detected while continuing transcription task {TaskId} at status {Status}. Breaking to avoid infinite loop.",
                            task.Id,
                            task.Status);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке задачи {TaskId}", task.Id);
                task.Status = OpenAiTranscriptionStatus.Error;
                task.Error = ex.Message;
                task.Done = true;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            return task;
        }

        private async Task RunDownloadStepAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.SourceFileUrl))
                throw new InvalidOperationException("Source file URL is not specified for the download step.");

            task.Status = OpenAiTranscriptionStatus.Downloading;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Downloading);

            try
            {
                await DownloadSourceFileAsync(task);

                task.Status = OpenAiTranscriptionStatus.Created;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                await CompleteStepAsync(step);
            }
            catch (Exception ex)
            {
                await FailStepAsync(step, ex.Message);
                throw;
            }
        }

        private async Task DownloadSourceFileAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.SourceFileUrl))
                throw new InvalidOperationException("Source file URL is missing.");

            var targetPath = task.SourceFilePath;
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new InvalidOperationException("Target file path is not specified.");

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(targetPath))
            {
                return;
            }

            var uri = new Uri(task.SourceFileUrl);

            if (_yandexDiskDownloadService.IsYandexDiskUrl(uri))
            {
                await using var downloadResult = await _yandexDiskDownloadService.DownloadAsync(uri);
                if (!downloadResult.Success || downloadResult.Response == null)
                {
                    var error = string.IsNullOrWhiteSpace(downloadResult.ErrorMessage)
                        ? "Unable to download file from the provided URL."
                        : downloadResult.ErrorMessage;
                    throw new InvalidOperationException(error);
                }

                await using var fileStream = File.Create(targetPath);
                await downloadResult.Response.Content.CopyToAsync(fileStream);
                return;
            }

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new InvalidOperationException($"Unable to download file from the provided URL. Status code: {status}");
            }

            await using var targetStream = File.Create(targetPath);
            await response.Content.CopyToAsync(targetStream);
        }

        private async Task RunConversionStepAsync(OpenAiTranscriptionTask task)
        {
            task.Status = OpenAiTranscriptionStatus.Converting;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Converting);

            try
            {
                var sourcePath = Path.GetFullPath(task.SourceFilePath);
                var outputPath = Path.GetFullPath(Path.Combine(_workingDirectory, $"{task.Id}.wav"));
                await _ffmpegService.ConvertToWav16kMonoAsync(sourcePath, outputPath);

                task.ConvertedFilePath = outputPath;
                task.Status = OpenAiTranscriptionStatus.Transcribing;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                await CompleteStepAsync(step);
            }
            catch (Exception ex)
            {
                await FailStepAsync(step, ex.Message);
                throw;
            }
        }

        private async Task RunTranscriptionStepAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.ConvertedFilePath) || !File.Exists(task.ConvertedFilePath))
                throw new InvalidOperationException("Converted audio file not found.");

            task.Status = OpenAiTranscriptionStatus.Transcribing;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Transcribing);

            try
            {
                var ffmpegExecutable = _ffmpegService.ResolveFfmpegExecutable();
                var transcription = await _whisperTranscriptionService.TranscribeAsync(
                    task.ConvertedFilePath!,
                    _workingDirectory,
                    ffmpegExecutable);
                task.RecognizedText = transcription.TimecodedText;
                task.SegmentsJson = transcription.RawJson;
                task.SegmentsTotal = 0;
                task.SegmentsProcessed = 0;
                task.Status = OpenAiTranscriptionStatus.Segmenting;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                await CompleteStepAsync(step);
            }
            catch (Exception ex)
            {
                await FailStepAsync(step, ex.Message);
                throw;
            }
        }

        private async Task RunSegmentingStepAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.SegmentsJson))
                throw new InvalidOperationException("No transcription segments available for processing.");

            task.Status = OpenAiTranscriptionStatus.Segmenting;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Segmenting);

            try
            {
                var parsed = WhisperTranscriptionHelper.Parse(task.SegmentsJson);
                if (parsed?.Segments == null || parsed.Segments.Count == 0)
                    throw new InvalidOperationException("Unable to parse segments for transcription task.");

                var captionSegments = BuildCaptionSegmentsFromWhisper(parsed);
                var processor = new CaptionProcessor();

                var recognitionProfile = await GetTaskRecognitionProfileNameAsync(task);
                var segmentBlockSize = await GetSegmentBlockSizeAsync(recognitionProfile);
                var maxWordsInSegment = segmentBlockSize.HasValue && segmentBlockSize.Value > 0
                    ? segmentBlockSize.Value
                    : int.MaxValue;
                var windowSize = 400;
                if (segmentBlockSize.HasValue && segmentBlockSize.Value > 0)
                {
                    windowSize = Math.Max(1, Math.Min(400, segmentBlockSize.Value));
                }

                var blocks = processor.SegmentCaptionSegmentsDetailed(
                    captionSegments,
                    maxWordsInSegment: maxWordsInSegment,
                    windowSize: windowSize,
                    pauseThreshold: 1.0);

                var existing = _dbContext.OpenAiRecognizedSegments.Where(s => s.TaskId == task.Id);
                _dbContext.OpenAiRecognizedSegments.RemoveRange(existing);
                await _dbContext.SaveChangesAsync();

                var newSegments = new List<OpenAiRecognizedSegment>();
                int order = 0;

                foreach (var block in blocks)
                {
                    if (string.IsNullOrWhiteSpace(block.Text))
                        continue;

                    var startIndex = ClampIndex(block.StartIndex, captionSegments.Count);
                    var endIndex = ClampIndex(block.EndIndex, captionSegments.Count);

                    var startSegment = captionSegments[startIndex];
                    var endSegment = captionSegments[endIndex];

                    var recognized = new OpenAiRecognizedSegment
                    {
                        TaskId = task.Id,
                        Order = order++,
                        Text = block.Text,
                        IsProcessed = false,
                        IsProcessing = false,
                        StartSeconds = startSegment.Time,
                        EndSeconds = endSegment.EndTime ?? endSegment.Time
                    };

                    newSegments.Add(recognized);
                }

                if (newSegments.Count == 0)
                    throw new InvalidOperationException("Segmenting produced no blocks to process.");

                await _dbContext.OpenAiRecognizedSegments.AddRangeAsync(newSegments);

                task.SegmentsTotal = newSegments.Count;
                task.SegmentsProcessed = 0;
                task.Status = OpenAiTranscriptionStatus.ProcessingSegments;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                await CompleteStepAsync(step);
            }
            catch (Exception ex)
            {
                await FailStepAsync(step, ex.Message);
                throw;
            }
        }

        private async Task RunSegmentProcessingStepAsync(OpenAiTranscriptionTask task)
        {
            task.Status = OpenAiTranscriptionStatus.ProcessingSegments;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await GetOrStartStepAsync(task, OpenAiTranscriptionStatus.ProcessingSegments);

            var stuck = _dbContext.OpenAiRecognizedSegments
                .Where(s => s.TaskId == task.Id && s.IsProcessing && !s.IsProcessed);
            foreach (var segment in stuck)
            {
                segment.IsProcessing = false;
            }
            await _dbContext.SaveChangesAsync();

            var next = await _dbContext.OpenAiRecognizedSegments
                .Where(s => s.TaskId == task.Id && !s.IsProcessed && !s.IsProcessing)
                .OrderBy(s => s.Order)
                .FirstOrDefaultAsync();

            if (next == null)
            {
                await CompleteSegmentProcessingAsync(task, step);
                return;
            }

            next.IsProcessing = true;
            await _dbContext.SaveChangesAsync();

            var succeeded = false;

            try
            {
                var previousContext = await _dbContext.OpenAiRecognizedSegments
                    .Where(s => s.TaskId == task.Id && s.IsProcessed && s.Order < next.Order)
                    .OrderBy(s => s.Order)
                    .Select(s => s.ProcessedText ?? s.Text)
                    .ToListAsync();

                string processedText;
                try
                {
                    var context = string.Join("\n", previousContext);
                    var recognitionProfile = await GetTaskRecognitionProfileNameAsync(task);
                    processedText = await _punctuationService.FixPunctuationAsync(
                        next.Text,
                        context,
                        recognitionProfile,
                        task.Clarification);
                }
                catch
                {
                    processedText = next.Text;
                }

                next.ProcessedText = processedText;
                next.IsProcessed = true;
                succeeded = true;
            }
            catch (Exception ex)
            {
                if (step.Status == OpenAiTranscriptionStepStatus.InProgress)
                {
                    await FailStepAsync(step, ex.Message);
                }
                throw;
            }
            finally
            {
                next.IsProcessing = false;
                task.SegmentsProcessed = await _dbContext.OpenAiRecognizedSegments
                    .CountAsync(s => s.TaskId == task.Id && s.IsProcessed);
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            if (succeeded)
            {
                var anyLeft = await _dbContext.OpenAiRecognizedSegments
                    .AnyAsync(s => s.TaskId == task.Id && !s.IsProcessed);

                if (!anyLeft)
                {
                    await CompleteSegmentProcessingAsync(task, step);
                }
            }
        }

      

        private async Task<OpenAiTranscriptionStep> StartStepAsync(OpenAiTranscriptionTask task, OpenAiTranscriptionStatus stepType)
        {
            var step = new OpenAiTranscriptionStep
            {
                TaskId = task.Id,
                Step = stepType,
                StartedAt = DateTime.UtcNow,
                Status = OpenAiTranscriptionStepStatus.InProgress
            };

            _dbContext.OpenAiTranscriptionSteps.Add(step);
            await _dbContext.SaveChangesAsync();
            return step;
        }

        private async Task CompleteStepAsync(OpenAiTranscriptionStep step)
        {
            step.Status = OpenAiTranscriptionStepStatus.Completed;
            step.FinishedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task FailStepAsync(OpenAiTranscriptionStep step, string error)
        {
            step.Status = OpenAiTranscriptionStepStatus.Error;
            step.Error = error;
            step.FinishedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task<OpenAiTranscriptionStep> GetOrStartStepAsync(OpenAiTranscriptionTask task, OpenAiTranscriptionStatus stepType)
        {
            var existing = await _dbContext.OpenAiTranscriptionSteps
                .Where(s => s.TaskId == task.Id && s.Step == stepType && s.Status == OpenAiTranscriptionStepStatus.InProgress)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            return existing ?? await StartStepAsync(task, stepType);
        }

        private async Task CompleteSegmentProcessingAsync(OpenAiTranscriptionTask task, OpenAiTranscriptionStep step)
        {
            if (step.Status == OpenAiTranscriptionStepStatus.InProgress)
            {
                await CompleteStepAsync(step);
            }

            var processedSegments = await _dbContext.OpenAiRecognizedSegments
                .Where(s => s.TaskId == task.Id)
                .OrderBy(s => s.Order)
                .Select(s => s.ProcessedText ?? s.Text)
                .ToListAsync();

            task.SegmentsProcessed = task.SegmentsTotal;
            task.ProcessedText = string.Join("\n", processedSegments);
            task.Status = OpenAiTranscriptionStatus.Done;            
            task.Done = true;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private static int ClampIndex(int value, int length)
        {
            if (length <= 0)
                return 0;
            if (value < 0)
                return 0;
            if (value >= length)
                return length - 1;
            return value;
        }

        private async Task<int?> GetSegmentBlockSizeAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return null;

            var blockSize = await _dbContext.RecognitionProfiles
                .AsNoTracking()
                .Where(p => p.Name == profileName)
                .Select(p => (int?)p.SegmentBlockSize)
                .FirstOrDefaultAsync();

            if (blockSize is null)
            {
                _logger.LogWarning(
                    "Recognition profile {ProfileName} not found while determining segment block size.",
                    profileName);
            }

            return blockSize;
        }

        private async Task<string> GetTaskRecognitionProfileNameAsync(OpenAiTranscriptionTask task)
        {
            if (task.RecognitionProfileId.HasValue)
            {
                var reference = _dbContext.Entry(task).Reference(t => t.RecognitionProfile);
                if (!reference.IsLoaded)
                {
                    await reference.LoadAsync();
                }

                var name = task.RecognitionProfile?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }

                _logger.LogWarning(
                    "Recognition profile with id {ProfileId} not found for task {TaskId}. Falling back to default profile.",
                    task.RecognitionProfileId,
                    task.Id);
            }

            return SegmentProcessingProfileName;
        }

        private static List<CaptionSegment> BuildCaptionSegmentsFromWhisper(WhisperTranscriptionResponse parsed)
        {
            var segments = new List<CaptionSegment>();

            if (parsed.Segments == null)
                return segments;

            foreach (var segment in parsed.Segments)
            {
                if (segment.Words != null && segment.Words.Count > 0)
                {
                    foreach (var word in segment.Words)
                    {
                        var text = (word.Word ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(text))
                            continue;

                        var start = word.Start ?? segment.Start;
                        var end = word.End ?? segment.End;

                        segments.Add(new CaptionSegment
                        {
                            Text = text,
                            WordCount = 1,
                            Time = start,
                            EndTime = end,
                            PauseBeforeNext = 0
                        });
                    }
                }
                else
                {
                    var text = (segment.Text ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0)
                        continue;

                    var duration = Math.Max(segment.End - segment.Start, 0);
                    var perWord = duration > 0 ? duration / words.Length : (double?)null;
                    var cursor = segment.Start;

                    foreach (var word in words)
                    {
                        var trimmed = word.Trim();
                        if (string.IsNullOrEmpty(trimmed))
                            continue;

                        double? end = perWord.HasValue ? cursor + perWord.Value : (double?)null;

                        segments.Add(new CaptionSegment
                        {
                            Text = trimmed,
                            WordCount = 1,
                            Time = cursor,
                            EndTime = end ?? cursor,
                            PauseBeforeNext = 0
                        });

                        if (perWord.HasValue)
                        {
                            cursor += perWord.Value;
                        }
                    }
                }
            }

            segments.Sort((a, b) => a.Time.CompareTo(b.Time));
            return segments;
        }

        private async Task PrepareTaskForContinuationAsync(OpenAiTranscriptionTask task)
        {
            if (task.Status != OpenAiTranscriptionStatus.Error)
            {
                return;
            }

            var stepsCollection = _dbContext.Entry(task).Collection(t => t.Steps);
            if (!stepsCollection.IsLoaded)
            {
                await stepsCollection.LoadAsync();
            }

            var lastErrorStep = task.Steps?
                .Where(s => s.Status == OpenAiTranscriptionStepStatus.Error)
                .OrderByDescending(s => s.StartedAt)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();

            var targetStatus = await DetermineContinuationStatusAsync(task, lastErrorStep);

            if (task.Status == targetStatus && !task.Done && string.IsNullOrEmpty(task.Error))
            {
                return;
            }

            task.Status = targetStatus;
            task.Done = false;
            task.Error = null;
            task.ModifiedAt = DateTime.UtcNow;

            if (targetStatus == OpenAiTranscriptionStatus.ProcessingSegments)
            {
                var stuckSegments = await _dbContext.OpenAiRecognizedSegments
                    .Where(s => s.TaskId == task.Id && s.IsProcessing)
                    .ToListAsync();

                if (stuckSegments.Count > 0)
                {
                    foreach (var segment in stuckSegments)
                    {
                        segment.IsProcessing = false;
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        private async Task<OpenAiTranscriptionStatus> DetermineContinuationStatusAsync(
            OpenAiTranscriptionTask task,
            OpenAiTranscriptionStep? lastErrorStep)
        {
            if (lastErrorStep != null)
            {
                return lastErrorStep.Step;
            }

            var segmentsCollection = _dbContext.Entry(task).Collection(t => t.Segments);
            if (!segmentsCollection.IsLoaded)
            {
                await segmentsCollection.LoadAsync();
            }

            if (!string.IsNullOrWhiteSpace(task.SourceFileUrl)
                && (string.IsNullOrWhiteSpace(task.SourceFilePath) || !File.Exists(task.SourceFilePath)))
            {
                return OpenAiTranscriptionStatus.Downloading;
            }           

            if (task.Segments?.Any() == true)
            {
                if (task.Segments.Any(s => !s.IsProcessed || s.IsProcessing))
                {
                    return OpenAiTranscriptionStatus.ProcessingSegments;
                }

               
            }

            if (!string.IsNullOrWhiteSpace(task.SegmentsJson) || !string.IsNullOrWhiteSpace(task.RecognizedText))
            {
                return OpenAiTranscriptionStatus.Segmenting;
            }

            if (!string.IsNullOrWhiteSpace(task.ConvertedFilePath))
            {
                return OpenAiTranscriptionStatus.Transcribing;
            }

            return OpenAiTranscriptionStatus.Converting;
        }

    }
}
