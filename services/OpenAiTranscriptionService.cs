using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.services.Whisper;

namespace YandexSpeech.services
{
    public class OpenAiTranscriptionService : IOpenAiTranscriptionService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<OpenAiTranscriptionService> _logger;
        private readonly string _ffmpegPath;
        private readonly string _openAiApiKey;
        private readonly string _workingDirectory;
        private const string FormattingModel = "gpt-4.1-mini";
        private readonly IWhisperTranscriptionService _whisperTranscriptionService;
        private readonly IPunctuationService _punctuationService;
        private const int FormattingMaxAttempts = 5;
        private static readonly TimeSpan FormattingRetryDelay = TimeSpan.FromSeconds(5);

        public OpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILogger<OpenAiTranscriptionService> logger,
            IPunctuationService punctuationService,
            IWhisperTranscriptionService whisperTranscriptionService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _punctuationService = punctuationService;
            _whisperTranscriptionService = whisperTranscriptionService;
            _ffmpegPath = configuration.GetValue<string>("FfmpegExePath")
                           ?? throw new InvalidOperationException("FfmpegExePath is not configured.");
            _openAiApiKey = configuration["OpenAI:ApiKey"]
                             ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            _workingDirectory = Path.Combine(Path.GetTempPath(), "openai-transcriptions");
            Directory.CreateDirectory(_workingDirectory);
        }

        public async Task<OpenAiTranscriptionTask> StartTranscriptionAsync(string sourceFilePath, string createdBy)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path must be provided.", nameof(sourceFilePath));

            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("Source file not found.", sourceFilePath);

            if (string.IsNullOrWhiteSpace(createdBy))
                throw new ArgumentException("Creator identifier must be provided.", nameof(createdBy));

            var task = new OpenAiTranscriptionTask
            {
                SourceFilePath = sourceFilePath,
                CreatedBy = createdBy,
                Status = OpenAiTranscriptionStatus.Created,
                Done = false,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
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
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return null;

            await PrepareTaskForContinuationAsync(task);

            try
            {
                var guard = 0;
                while (guard++ < 6)
                {
                    switch (task.Status)
                    {
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
                        case OpenAiTranscriptionStatus.Formatting:
                            await RunFormattingStepAsync(task);
                            break;
                        default:
                            return task;
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
                var ffmpegExecutable = ResolveFfmpegExecutable();

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("-y");
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(sourcePath);
                startInfo.ArgumentList.Add("-vn");
                startInfo.ArgumentList.Add("-ac");
                startInfo.ArgumentList.Add("1");
                startInfo.ArgumentList.Add("-ar");
                startInfo.ArgumentList.Add("16000");
                startInfo.ArgumentList.Add("-acodec");
                startInfo.ArgumentList.Add("pcm_s16le");
                startInfo.ArgumentList.Add(outputPath);

                using var process = new Process { StartInfo = startInfo };

                process.Start();

                var waitForExitTask = process.WaitForExitAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                await Task.WhenAll(waitForExitTask, standardErrorTask, standardOutputTask);

                var standardError = await standardErrorTask;
                var standardOutput = await standardOutputTask;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"FFmpeg conversion failed (exit code {process.ExitCode}): {standardError}\n{standardOutput}");
                }

                if (!File.Exists(outputPath))
                {
                    throw new InvalidOperationException($"FFmpeg conversion did not produce an output file at '{outputPath}'. Output: {standardOutput} Error: {standardError}");
                }

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
                var ffmpegExecutable = ResolveFfmpegExecutable();
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
                var blocks = processor.SegmentCaptionSegmentsDetailed(
                    captionSegments,
                    maxWordsInSegment: 4000,
                    windowSize: 400,
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
                    processedText = await _punctuationService.FixPunctuationAsync(next.Text, context);
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

        private async Task RunFormattingStepAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.ProcessedText))
                throw new InvalidOperationException("No processed transcription text is available for formatting.");

            task.Status = OpenAiTranscriptionStatus.Formatting;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Formatting);

            try
            {
                var markdown = await CreateDialogueMarkdownAsync(task.ProcessedText!);
                task.MarkdownText = markdown;
                task.Status = OpenAiTranscriptionStatus.Done;
                task.Done = true;
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
            task.Status = OpenAiTranscriptionStatus.Formatting;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private string ResolveFfmpegExecutable()
        {
            var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

            if (File.Exists(_ffmpegPath))
                return _ffmpegPath;

            if (Directory.Exists(_ffmpegPath))
            {
                var candidate = Path.Combine(_ffmpegPath, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return executableName;
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

        private async Task<string> CreateDialogueMarkdownAsync(string transcription)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(50) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You format transcripts into Markdown dialogue. Identify speakers and label them with bold names (either real names found in the text or Speaker 1, Speaker 2, etc.). Each replica must begin with the speaker label followed by a colon. Do not add commentary or analysis."
                },
                new { role = "user", content = transcription }
            };

            var body = new
            {
                model = FormattingModel,
                messages,
                temperature = 0.2
            };

            for (var attempt = 1; attempt <= FormattingMaxAttempts; attempt++)
            {
                try
                {
                    using var requestContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", requestContent);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        throw new InvalidOperationException($"OpenAI formatting failed: {error}");
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0)
                        throw new InvalidOperationException("OpenAI formatting response did not contain choices.");

                    var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                    if (string.IsNullOrWhiteSpace(content))
                        throw new InvalidOperationException("OpenAI formatting response is empty.");

                    return content.Trim();
                }
                catch (HttpRequestException ex) when (attempt < FormattingMaxAttempts)
                {
                    _logger.LogWarning(ex, "Не удалось связаться с OpenAI для форматирования (попытка {Attempt}/{Total}).", attempt, FormattingMaxAttempts);
                }
                catch (TaskCanceledException ex) when (attempt < FormattingMaxAttempts)
                {
                    _logger.LogWarning(ex, "Запрос на форматирование OpenAI завершился по таймауту (попытка {Attempt}/{Total}).", attempt, FormattingMaxAttempts);
                }
                catch (InvalidOperationException ex) when (attempt < FormattingMaxAttempts && ex.Message.StartsWith("OpenAI formatting failed", StringComparison.Ordinal))
                {
                    _logger.LogWarning(ex, "OpenAI вернул ошибку при форматировании (попытка {Attempt}/{Total}).", attempt, FormattingMaxAttempts);
                }

                await Task.Delay(TimeSpan.FromTicks(FormattingRetryDelay.Ticks * attempt));
            }

            throw new InvalidOperationException($"OpenAI formatting failed after {FormattingMaxAttempts} attempts.");
        }

        private async Task PrepareTaskForContinuationAsync(OpenAiTranscriptionTask task)
        {
            if (task.Status != OpenAiTranscriptionStatus.Error)
            {
                return;
            }

            if (task.Steps == null || !task.Steps.Any())
            {
                await _dbContext.Entry(task).Collection(t => t.Steps).LoadAsync();
            }

            var lastErrorStep = task.Steps?
                .Where(s => s.Status == OpenAiTranscriptionStepStatus.Error)
                .OrderByDescending(s => s.StartedAt)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();

            var targetStatus = lastErrorStep?.Step ?? OpenAiTranscriptionStatus.Created;

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

    }
}
