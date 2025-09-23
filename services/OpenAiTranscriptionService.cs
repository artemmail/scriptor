using System;
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

        public OpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILogger<OpenAiTranscriptionService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
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
                task.RecognizedText = transcription;
                task.Status = OpenAiTranscriptionStatus.Formatting;
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

        private async Task RunFormattingStepAsync(OpenAiTranscriptionTask task)
        {
            if (string.IsNullOrWhiteSpace(task.RecognizedText))
                throw new InvalidOperationException("No transcription text is available for formatting.");

            task.Status = OpenAiTranscriptionStatus.Formatting;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var step = await StartStepAsync(task, OpenAiTranscriptionStatus.Formatting);

            try
            {
                var markdown = await CreateDialogueMarkdownAsync(task.RecognizedText!);
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

        private async Task<string> TranscribeWithOpenAiAsync(string audioFilePath)
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

                var transcriptFile = Directory.EnumerateFiles(outputDirectory, "*.txt").FirstOrDefault();
                if (transcriptFile == null)
                {
                    var output = await standardOutputTask;
                    throw new InvalidOperationException($"Whisper transcription did not produce a text file. Output: {output}");
                }

                var transcription = await File.ReadAllTextAsync(transcriptFile);
                if (string.IsNullOrWhiteSpace(transcription))
                    throw new InvalidOperationException("Whisper transcription result is empty.");

                return transcription.Trim();
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
            builder.Append($"\"{audioFilePath}\" --model {_whisperModel} --task transcribe --output_format txt --output_dir \"{outputDirectory}\"");
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
    }
}
