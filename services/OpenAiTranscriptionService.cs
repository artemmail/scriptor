using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;

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
        private readonly string _whisperExecutableSetting;
        private readonly string _whisperModel;
        private readonly string _whisperDevice;
        private readonly IPunctuationService _punctuationService;
        private static readonly JsonSerializerOptions WhisperJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public OpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILogger<OpenAiTranscriptionService> logger,
            IPunctuationService punctuationService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _punctuationService = punctuationService;
            _ffmpegPath = configuration.GetValue<string>("FfmpegExePath")
                           ?? throw new InvalidOperationException("FfmpegExePath is not configured.");
            _openAiApiKey = configuration["OpenAI:ApiKey"]
                             ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            _workingDirectory = Path.Combine(Path.GetTempPath(), "openai-transcriptions");
            Directory.CreateDirectory(_workingDirectory);

            var whisperSection = configuration.GetSection("Whisper");
            _whisperExecutableSetting = whisperSection.GetValue<string>("ExecutablePath") ?? "whisper";
            _whisperModel = whisperSection.GetValue<string>("Model") ?? "medium";

            var configuredDevice = whisperSection.GetValue<string>("Device");
            if (!string.IsNullOrWhiteSpace(configuredDevice))
            {
                _whisperDevice = configuredDevice;
            }
            else
            {
                var useGpu = whisperSection.GetValue<bool?>("UseGpu") ?? false;
                _whisperDevice = useGpu ? "cuda" : "cpu";
            }
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

        public async Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId)
        {
            var task = await _dbContext.OpenAiTranscriptionTasks
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                return null;

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
                var outputPath = Path.Combine(_workingDirectory, $"{task.Id}.wav");
                var ffmpegExecutable = ResolveFfmpegExecutable();
                var arguments = BuildFfmpegArguments(task.SourceFilePath, outputPath);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegExecutable,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                var standardErrorTask = process.StandardError.ReadToEndAsync();
                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                await Task.WhenAll(process.WaitForExitAsync(), standardErrorTask, standardOutputTask);

                if (process.ExitCode != 0)
                {
                    var error = await standardErrorTask;
                    throw new InvalidOperationException($"FFmpeg conversion failed (exit code {process.ExitCode}): {error}");
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
                var transcription = await TranscribeWithOpenAiAsync(task.ConvertedFilePath!);
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
                var parsed = ParseWhisperResult(task.SegmentsJson);
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

        private static string BuildFfmpegArguments(string source, string target)
        {
            return $"-y -i \"{source}\" -vn -ac 1 -ar 16000 -acodec pcm_s16le \"{target}\"";
        }

        private async Task<WhisperTranscriptionResult> TranscribeWithOpenAiAsync(string audioFilePath)
        {
            var whisperExecutable = ResolveWhisperExecutable();
            var outputDirectory = Path.Combine(_workingDirectory, $"whisper-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDirectory);
            var arguments = BuildWhisperArguments(audioFilePath, outputDirectory);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whisperExecutable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();

                var waitForExitTask = process.WaitForExitAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                var standardOutputTask = process.StandardOutput.ReadToEndAsync();

                await Task.WhenAll(waitForExitTask, standardErrorTask, standardOutputTask);

                if (process.ExitCode != 0)
                {
                    var error = await standardErrorTask;
                    throw new InvalidOperationException($"Whisper transcription failed (exit code {process.ExitCode}): {error}");
                }

                var transcriptFile = Directory.EnumerateFiles(outputDirectory, "*.json").FirstOrDefault();
                if (transcriptFile == null)
                {
                    var output = await standardOutputTask;
                    throw new InvalidOperationException($"Whisper transcription did not produce a text file. Output: {output}");
                }

                var transcription = await File.ReadAllTextAsync(transcriptFile);
                if (string.IsNullOrWhiteSpace(transcription))
                    throw new InvalidOperationException("Whisper transcription result is empty.");

                var parsed = ParseWhisperResult(transcription);
                if (parsed?.Segments == null || parsed.Segments.Count == 0)
                    throw new InvalidOperationException("Whisper transcription does not contain segments.");

                var builder = new StringBuilder();
                foreach (var segment in parsed.Segments)
                {
                    var start = TimeSpan.FromSeconds(segment.Start);
                    var end = TimeSpan.FromSeconds(segment.End);
                    var text = (segment.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    builder.Append('[')
                        .Append(FormatTimestamp(start))
                        .Append(" --> ")
                        .Append(FormatTimestamp(end))
                        .Append("] ")
                        .AppendLine(text);
                }

                var timecodedText = builder.ToString().Trim();

                return new WhisperTranscriptionResult
                {
                    TimecodedText = timecodedText,
                    RawJson = transcription
                };
            }
            finally
            {
                try
                {
                    Directory.Delete(outputDirectory, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove Whisper output directory {OutputDirectory}", outputDirectory);
                }
            }
        }

        private string ResolveWhisperExecutable()
        {
            if (File.Exists(_whisperExecutableSetting))
                return _whisperExecutableSetting;

            if (Directory.Exists(_whisperExecutableSetting))
            {
                var executableName = OperatingSystem.IsWindows() ? "whisper.exe" : "whisper";
                var candidate = Path.Combine(_whisperExecutableSetting, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return _whisperExecutableSetting;
        }

        private string BuildWhisperArguments(string audioFilePath, string outputDirectory)
        {
            var builder = new StringBuilder();
            builder.Append($"\"{audioFilePath}\" --model {_whisperModel} --task transcribe --output_format json --word_timestamps True --output_dir \"{outputDirectory}\"");
            if (!string.IsNullOrWhiteSpace(_whisperDevice))
            {
                builder.Append($" --device {_whisperDevice}");
            }

            if (string.Equals(_whisperDevice, "cpu", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" --fp16 False");
            }

            return builder.ToString();
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

        private static string FormatTimestamp(TimeSpan time) => time.ToString(@"hh\:mm\:ss\.fff");

        private static WhisperTranscriptionResponse? ParseWhisperResult(string json)
        {
            return JsonSerializer.Deserialize<WhisperTranscriptionResponse>(json, WhisperJsonOptions);
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

            using var requestContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", requestContent);
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

        private sealed class WhisperTranscriptionResult
        {
            public string TimecodedText { get; init; } = string.Empty;
            public string RawJson { get; init; } = string.Empty;
        }

        private sealed class WhisperTranscriptionResponse
        {
            [JsonPropertyName("segments")]
            public List<WhisperSegment> Segments { get; set; } = new();
        }

        private sealed class WhisperSegment
        {
            [JsonPropertyName("start")]
            public double Start { get; set; }

            [JsonPropertyName("end")]
            public double End { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("words")]
            public List<WhisperWord>? Words { get; set; }
        }

        private sealed class WhisperWord
        {
            [JsonPropertyName("word")]
            public string? Word { get; set; }

            [JsonPropertyName("start")]
            public double? Start { get; set; }

            [JsonPropertyName("end")]
            public double? End { get; set; }
        }
    }
}
