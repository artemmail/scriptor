using System;
using System.Diagnostics;
using System.IO;
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
        private const string TranscriptionModel = "gpt-4o-mini-transcribe";
        private const string FormattingModel = "gpt-4.1-mini";

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
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            await using var fileStream = File.OpenRead(audioFilePath);
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(fileContent, "file", Path.GetFileName(audioFilePath));
            content.Add(new StringContent(TranscriptionModel), "model");

            var response = await client.PostAsync("https://api.openai.com/v1/audio/transcriptions", content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"OpenAI transcription failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            throw new InvalidOperationException("OpenAI transcription response did not contain text.");
        }

        private async Task<string> CreateDialogueMarkdownAsync(string transcription)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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
