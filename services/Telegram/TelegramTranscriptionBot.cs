using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YandexSpeech.services.Options;
using YandexSpeech.services.Whisper;
using IOFile = System.IO.File;

namespace YandexSpeech.services.Telegram
{
    public sealed class TelegramTranscriptionBot : BackgroundService
    {
        private readonly ILogger<TelegramTranscriptionBot> _logger;
        private readonly IOptionsMonitor<TelegramBotOptions> _optionsMonitor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly FasterWhisperQueueClient _queueClient;
        private readonly string _model;
        private readonly string _device;
        private readonly string _computeType;
        private readonly string _language;
        private readonly string _temperatureLiteral;
        private readonly string _compressionLiteral;
        private readonly string _logProbLiteral;
        private readonly string _noSpeechLiteral;
        private readonly string _conditionLiteral;
        private readonly string? _ffmpegExecutable;
        private readonly JsonSerializerOptions _logJsonOptions;
        private readonly object _logLock = new();

        private ITelegramBotClient? _botClient;

        public TelegramTranscriptionBot(
            IOptionsMonitor<TelegramBotOptions> optionsMonitor,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            FasterWhisperQueueClient queueClient,
            ILogger<TelegramTranscriptionBot> logger)
        {
            _optionsMonitor = optionsMonitor;
            _httpClientFactory = httpClientFactory;
            _queueClient = queueClient;
            _logger = logger;

            var section = configuration.GetSection("FasterWhisper");
            _model = section.GetValue<string>("Model") ?? configuration.GetValue<string>("Whisper:Model") ?? "medium";
            _device = section.GetValue<string>("Device") ?? configuration.GetValue<string>("Whisper:Device") ?? "cpu";
            _computeType = section.GetValue<string>("ComputeType") ?? "int8";
            _language = section.GetValue<string>("Language") ?? configuration.GetValue<string>("Whisper:Language") ?? "ru";
            var temperatures = ParseTemperatures(section) ?? new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 };
            _temperatureLiteral = BuildTemperatureLiteral(temperatures);
            var compression = section.GetValue<double?>("CompressionRatioThreshold") ?? 2.4;
            var logProb = section.GetValue<double?>("LogProbThreshold") ?? -1.0;
            var noSpeech = section.GetValue<double?>("NoSpeechThreshold") ?? 0.6;
            var condition = section.GetValue<bool?>("ConditionOnPreviousText") ?? true;
            _compressionLiteral = FormatDouble(compression);
            _logProbLiteral = FormatDouble(logProb);
            _noSpeechLiteral = FormatDouble(noSpeech);
            _conditionLiteral = condition ? "True" : "False";
            _ffmpegExecutable = section.GetValue<string?>("FfmpegExecutable")
                ?? configuration.GetValue<string?>("Whisper:FfmpegExecutable");

            _logJsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var options = _optionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                _logger.LogInformation("Telegram bot is disabled via configuration.");
                await WaitForCancellationAsync(stoppingToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(options.BotToken))
            {
                _logger.LogWarning("Telegram bot token is not configured.");
                await WaitForCancellationAsync(stoppingToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(options.WebhookUrl))
            {
                _logger.LogWarning("Telegram webhook URL is not configured. Set Telegram:WebhookUrl in appsettings.");
                await WaitForCancellationAsync(stoppingToken);
                return;
            }

            var botOptions = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                ? new TelegramBotClientOptions(options.BotToken)
                : new TelegramBotClientOptions(options.BotToken, options.ApiBaseUrl);

            var httpClient = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot));
            _botClient = new TelegramBotClient(botOptions, httpClient);

            try
            {
                var me = await _botClient.GetMeAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Telegram bot connected as @{Username} (id {Id}).", me.Username, me.Id);

                await _botClient.DeleteWebhookAsync(true, cancellationToken: stoppingToken).ConfigureAwait(false);
                await _botClient.SetWebhookAsync(
                    url: options.WebhookUrl!,
                    allowedUpdates: new[] { UpdateType.Message },
                    secretToken: string.IsNullOrWhiteSpace(options.WebhookSecretToken) ? null : options.WebhookSecretToken,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("Telegram webhook configured at {WebhookUrl}.", options.WebhookUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Telegram bot webhook.");
                await WaitForCancellationAsync(stoppingToken);
                return;
            }

            await WaitForCancellationAsync(stoppingToken);
        }

        public bool ValidateSecretToken(string? providedToken)
        {
            var expected = _optionsMonitor.CurrentValue.WebhookSecretToken;
            if (string.IsNullOrEmpty(expected))
            {
                return true;
            }

            if (string.IsNullOrEmpty(providedToken))
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(providedToken);

            var isEqual = expectedBytes.Length == providedBytes.Length
                          && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(providedBytes);

            return isEqual;
        }

        public bool IsReady => _botClient is not null;

        public async Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            if (update.Message is not Message message)
            {
                return;
            }

            try
            {
                if (message.Text is string text && text.StartsWith("/"))
                {
                    await HandleCommandAsync(message, text, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (HasAudioPayload(message))
                {
                    await HandleAudioAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process Telegram update {UpdateId}.", update.Id);
            }
        }

        private async Task HandleCommandAsync(Message message, string text, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var rawCommand = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                              ?? text;
            var command = rawCommand.Split('@')[0];
            switch (command)
            {
                case "/start":
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Пришлите voice или аудиофайл — распознаю локально (GPU).",
                        replyParameters: new ReplyParameters { MessageId = message.MessageId },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
                case "/model":
                    var info = $"faster-whisper: {_model} | device: {_device} | dtype: {NormalizeCt2(_device, _computeType)}\nVAD: off";
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: info,
                        replyParameters: new ReplyParameters { MessageId = message.MessageId },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private async Task HandleAudioAsync(Message message, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            LogEvent("incoming", message, message.Caption ?? string.Empty, null);

            await _botClient.SendChatActionAsync(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            Message? status = null;
            try
            {
                status = await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "⏳ Обрабатываю…",
                    replyParameters: new ReplyParameters { MessageId = message.MessageId },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "scriptor-telegram", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var sourcePath = await DownloadMediaAsync(message, tempRoot, cancellationToken).ConfigureAwait(false);
                if (sourcePath is null)
                {
                    await EditStatusAsync(status, "Не нашёл голос/аудио в сообщении.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await EditStatusAsync(status, "🎛 Конвертация в WAV 16 кГц mono…", cancellationToken).ConfigureAwait(false);
                var wavPath = await ConvertToWav16kMonoAsync(sourcePath, tempRoot, cancellationToken).ConfigureAwait(false);

                await EditStatusAsync(status, "🎧 Распознаю через очередь…", cancellationToken).ConfigureAwait(false);
                var transcript = await TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(transcript.Text))
                {
                    await EditStatusAsync(status, "Не удалось распознать речь.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await DeleteStatusAsync(status, cancellationToken).ConfigureAwait(false);

                var probability = transcript.LanguageProbability.HasValue
                    ? transcript.LanguageProbability.Value.ToString("0.00", CultureInfo.InvariantCulture)
                    : "?";
                var header =
                    $"📝 Готово. Язык: {transcript.Language ?? "?"} (p={probability}).\nМодель: {_model} ({_device}, dtype {NormalizeCt2(_device, _computeType)}).";

                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: header,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await SendTranscriptAsync(message, transcript.Text, tempRoot, cancellationToken).ConfigureAwait(false);

                LogEvent("transcript", message, transcript.Text, new
                {
                    language = transcript.Language,
                    language_probability = transcript.LanguageProbability,
                    model = _model
                });
            }
            catch (OperationCanceledException)
            {
                if (status is not null)
                {
                    await DeleteStatusAsync(status, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process audio message {MessageId}.", message.MessageId);
                var errorText = $"⚠️ Ошибка: {ex.Message}";
                await EditStatusAsync(status, errorText, cancellationToken).ConfigureAwait(false);
                LogEvent("error", message, ex.Message, null);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private async Task<string?> DownloadMediaAsync(Message message, string directory, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return null;
            }

            string? fileId = null;
            string? fileName = null;

            if (message.Voice is { } voice)
            {
                fileId = voice.FileId;
                fileName = $"{voice.FileUniqueId}.oga";
            }
            else if (message.Audio is { } audio)
            {
                fileId = audio.FileId;
                fileName = !string.IsNullOrWhiteSpace(audio.FileName)
                    ? Path.GetFileName(audio.FileName)
                    : $"{audio.FileUniqueId}.mp3";
            }
            else if (message.VideoNote is { } videoNote)
            {
                fileId = videoNote.FileId;
                fileName = $"{videoNote.FileUniqueId}.mp4";
            }
            else if (message.Document is { } document &&
                     !string.IsNullOrWhiteSpace(document.MimeType) &&
                     document.MimeType.StartsWith("audio", StringComparison.OrdinalIgnoreCase))
            {
                fileId = document.FileId;
                fileName = !string.IsNullOrWhiteSpace(document.FileName)
                    ? Path.GetFileName(document.FileName)
                    : $"{document.FileUniqueId}.bin";
            }

            if (string.IsNullOrWhiteSpace(fileId))
            {
                return null;
            }

            var file = await _botClient.GetFileAsync(fileId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var extension = Path.GetExtension(file.FilePath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(extension) && (fileName is null || Path.GetExtension(fileName) is ""))
            {
                fileName = Path.ChangeExtension(fileName ?? file.FileId ?? Guid.NewGuid().ToString("N"), extension);
            }

            fileName ??= Path.GetFileName(file.FilePath ?? $"{Guid.NewGuid():N}.bin");
            var destination = Path.Combine(directory, fileName);

            await using var fs = IOFile.Create(destination);
            await _botClient.DownloadFile(file.FilePath!, fs, cancellationToken: cancellationToken).ConfigureAwait(false);
            return destination;
        }

        private async Task<string> ConvertToWav16kMonoAsync(string sourcePath, string directory, CancellationToken cancellationToken)
        {
            var ffmpeg = _optionsMonitor.CurrentValue.FfmpegExecutable;
            if (string.IsNullOrWhiteSpace(ffmpeg))
            {
                ffmpeg = _ffmpegExecutable;
            }
            if (string.IsNullOrWhiteSpace(ffmpeg))
            {
                ffmpeg = "ffmpeg";
            }

            var outputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + "_16k.wav");
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add("16000");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("pcm_s16le");
            startInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Не удалось запустить ffmpeg: {ex.Message}", ex);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "ffmpeg завершился с ошибкой"
                    : error.Trim());
            }

            return outputPath;
        }

        private async Task<(string Text, string? Language, double? LanguageProbability)> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            var request = new FasterWhisperQueueRequest
            {
                Audio = audioPath,
                Model = _model,
                Device = _device,
                ComputeType = NormalizeCt2(_device, _computeType),
                Language = _language,
                Temperature = _temperatureLiteral,
                CompressionRatioThreshold = _compressionLiteral,
                LogProbThreshold = _logProbLiteral,
                NoSpeechThreshold = _noSpeechLiteral,
                ConditionOnPreviousText = _conditionLiteral,
                FfmpegExecutable = _optionsMonitor.CurrentValue.FfmpegExecutable ?? _ffmpegExecutable
            };

            var response = await _queueClient.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                var message = string.IsNullOrWhiteSpace(response.Error) ? "Unknown error" : response.Error;
                throw new InvalidOperationException($"FasterWhisper transcription failed: {message}");
            }

            if (string.IsNullOrWhiteSpace(response.TranscriptJson))
            {
                throw new InvalidOperationException("FasterWhisper transcription result is empty.");
            }

            return ParseTranscript(response.TranscriptJson);
        }

        private async Task SendTranscriptAsync(Message originalMessage, string text, string workingDirectory, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var limit = _optionsMonitor.CurrentValue.MessageChunkLimit;
            if (limit <= 0)
            {
                limit = 3900;
            }

            text = text.Trim();
            if (text.Length == 0)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: originalMessage.Chat.Id,
                    text: "Пустой результат.",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            if (text.Length <= limit)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: originalMessage.Chat.Id,
                    text: text,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            var fileName = $"transcript_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.txt";
            var filePath = Path.Combine(workingDirectory, fileName);
            await IOFile.WriteAllTextAsync(filePath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            await using var stream = IOFile.OpenRead(filePath);
            await _botClient.SendDocumentAsync(
                chatId: originalMessage.Chat.Id,
                document: InputFile.FromStream(stream, fileName),
                caption: "🧾 Текст не поместился — отправляю .txt",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private static (string Text, string? Language, double? LanguageProbability) ParseTranscript(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? language = null;
            double? probability = null;
            var builder = new StringBuilder();

            if (root.TryGetProperty("language", out var langElement) && langElement.ValueKind == JsonValueKind.String)
            {
                language = langElement.GetString();
            }

            if (root.TryGetProperty("language_probability", out var probElement) && probElement.ValueKind == JsonValueKind.Number)
            {
                if (probElement.TryGetDouble(out var parsed))
                {
                    probability = parsed;
                }
            }

            if (root.TryGetProperty("segments", out var segmentsElement) && segmentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var segment in segmentsElement.EnumerateArray())
                {
                    if (segment.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        var segmentText = (textElement.GetString() ?? string.Empty).Trim();
                        if (segmentText.Length > 0)
                        {
                            if (builder.Length > 0)
                            {
                                builder.Append(' ');
                            }

                            builder.Append(segmentText);
                        }
                    }
                }
            }

            return (builder.ToString().Trim(), language, probability);
        }

        private async Task EditStatusAsync(Message? status, string text, CancellationToken cancellationToken)
        {
            if (status is null || _botClient is null)
            {
                return;
            }

            try
            {
                await _botClient.EditMessageTextAsync(
                    chatId: status.Chat.Id,
                    messageId: status.MessageId,
                    text: text,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to edit Telegram status message {MessageId}.", status.MessageId);
            }
        }

        private async Task DeleteStatusAsync(Message? status, CancellationToken cancellationToken)
        {
            if (status is null || _botClient is null)
            {
                return;
            }

            try
            {
                await _botClient.DeleteMessageAsync(status.Chat.Id, status.MessageId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to delete Telegram status message {MessageId}.", status.MessageId);
            }
        }

        private void LogEvent(string kind, Message message, string text, object? extra)
        {
            var options = _optionsMonitor.CurrentValue;
            var path = string.IsNullOrWhiteSpace(options.LogFilePath)
                ? Path.Combine(AppContext.BaseDirectory, "telegram_messages.log")
                : Path.GetFullPath(options.LogFilePath);

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var record = new
                {
                    ts = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    chat_id = message.Chat.Id,
                    user = new
                    {
                        id = message.From?.Id,
                        full_name = BuildFullName(message.From),
                        username = message.From?.Username
                    },
                    kind,
                    text,
                    extra
                };

                var line = JsonSerializer.Serialize(record, _logJsonOptions);
                lock (_logLock)
                {
                    IOFile.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to append Telegram log entry to {Path}.", path);
            }
        }

        private static string? BuildFullName(User? user)
        {
            if (user is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(user.LastName))
            {
                return string.Join(' ', new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            return user.FirstName;
        }

        private static bool HasAudioPayload(Message message)
        {
            return message.Voice is not null
                   || message.Audio is not null
                   || message.VideoNote is not null
                   || (message.Document is not null
                       && !string.IsNullOrWhiteSpace(message.Document.MimeType)
                       && message.Document.MimeType.StartsWith("audio", StringComparison.OrdinalIgnoreCase));
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static async Task WaitForCancellationAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
        }

        private static IReadOnlyList<double>? ParseTemperatures(IConfiguration section)
        {
            var array = section.GetSection("Temperatures").Get<double[]?>();
            if (array is { Length: > 0 })
            {
                return Array.AsReadOnly(array);
            }

            var temperaturesValue = section.GetValue<string?>("Temperatures");
            if (string.IsNullOrWhiteSpace(temperaturesValue))
            {
                return null;
            }

            var parts = temperaturesValue.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            var values = new List<double>();
            foreach (var part in parts)
            {
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    values.Add(parsed);
                }
            }

            return values.Count == 0 ? null : values.AsReadOnly();
        }

        private static string BuildTemperatureLiteral(IReadOnlyCollection<double> temperatures)
        {
            if (temperatures.Count == 0)
            {
                return "0.0";
            }

            if (temperatures.Count == 1)
            {
                return FormatDouble(temperatures.First());
            }

            return "(" + string.Join(", ", temperatures.Select(FormatDouble)) + ")";
        }

        private static string FormatDouble(double value)
        {
            var formatted = value.ToString("0.###############################", CultureInfo.InvariantCulture);
            if (!formatted.Contains('.') && !formatted.Contains('e') && !formatted.Contains('E'))
            {
                formatted += ".0";
            }

            return formatted;
        }

        private static string NormalizeCt2(string device, string computeType)
        {
            var d = (device ?? "cpu").Trim().ToLowerInvariant();
            var ct = (computeType ?? string.Empty).Trim().ToLowerInvariant();

            if (d == "cuda" || d == "gpu" || d.StartsWith("cuda:"))
            {
                return ct switch
                {
                    "float16" => "float16",
                    "int8_float16" => "int8_float16",
                    "float32" => "float32",
                    _ => "float16"
                };
            }

            return ct switch
            {
                "int8" => "int8",
                "float32" => "float32",
                "float16" => "float16",
                _ => "int8"
            };
        }
    }
}
