using System;
using System.Collections.Generic;
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
using System.Net.Http.Headers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;
using YandexSpeech.services.Whisper;
using System.Text.RegularExpressions;
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
        private readonly IFfmpegService _ffmpegService;
        private readonly JsonSerializerOptions _logJsonOptions;
        private readonly object _logLock = new();
        private readonly string? _globalOpenAiApiKey;

        private const string DefaultOpenAiModel = "gpt-4.1";
        private const int DefaultSummaryThreshold = 70;
        private const string OpenAiEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string OpenAiSystemPrompt =
            """You are a meticulous editor for Telegram voice transcriptions. Fix punctuation, casing, and obvious ASR mistakes without adding content. Keep the input language. Output JSON with fields: polished, summary.""";

        private static readonly JsonSerializerOptions OpenAiRequestJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly HashSet<string> AudioFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aac",
            ".amr",
            ".flac",
            ".m4a",
            ".mp3",
            ".oga",
            ".ogg",
            ".opus",
            ".wav",
            ".weba",
            ".wma"
        };

        private static readonly HashSet<string> VideoFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".3gp",
            ".avi",
            ".m4v",
            ".mkv",
            ".mov",
            ".mp4",
            ".mpeg",
            ".mpg",
            ".webm"
        };

        private CancellationToken _stoppingToken = CancellationToken.None;

        private ITelegramBotClient? _botClient;

        private readonly record struct OpenAiPostProcessingResult(
            string Text,
            string? Summary,
            string? Model,
            string? Error,
            bool Attempted);

        public TelegramTranscriptionBot(
            IOptionsMonitor<TelegramBotOptions> optionsMonitor,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            FasterWhisperQueueClient queueClient,
            ILogger<TelegramTranscriptionBot> logger,
            IFfmpegService ffmpegService)
        {
            _optionsMonitor = optionsMonitor;
            _httpClientFactory = httpClientFactory;
            _queueClient = queueClient;
            _logger = logger;
            _ffmpegService = ffmpegService;

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
                ?? configuration.GetValue<string?>("Whisper:FfmpegExecutable")
                ?? configuration.GetValue<string?>("FfmpegExecutable");

            _logJsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            _globalOpenAiApiKey = configuration["OpenAI:ApiKey"];
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            var options = _optionsMonitor.CurrentValue;
            var useLongPolling = options.UseLongPolling;
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

            var botOptions = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                ? new TelegramBotClientOptions(options.BotToken)
                : new TelegramBotClientOptions(options.BotToken, options.ApiBaseUrl);

            var httpClient = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot));
            _botClient = new TelegramBotClient(botOptions, httpClient);

            try
            {
                var me = await _botClient.GetMeAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Telegram bot connected as @{Username} (id {Id}).", me.Username, me.Id);

                var webhookUrl = options.WebhookUrl;
                var dropPendingUpdates = useLongPolling || string.IsNullOrWhiteSpace(webhookUrl);
                await _botClient.DeleteWebhookAsync(dropPendingUpdates, cancellationToken: stoppingToken)
                    .ConfigureAwait(false);

                if (useLongPolling || string.IsNullOrWhiteSpace(webhookUrl))
                {
                    if (!useLongPolling)
                    {
                        _logger.LogWarning("Telegram webhook URL is not configured. Falling back to long polling mode.");
                    }

                    _logger.LogInformation("Telegram long polling mode enabled.");
                    await RunLongPollingAsync(stoppingToken).ConfigureAwait(false);
                    return;
                }

                try
                {
                    await _botClient.SetWebhookAsync(
                        url: webhookUrl!,
                        allowedUpdates: new[] { UpdateType.Message },
                        secretToken: string.IsNullOrWhiteSpace(options.WebhookSecretToken) ? null : options.WebhookSecretToken,
                        cancellationToken: stoppingToken).ConfigureAwait(false);

                    _logger.LogInformation("Telegram webhook configured at {WebhookUrl}.", webhookUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to configure Telegram webhook. Falling back to long polling mode.");
                    _logger.LogInformation("Telegram long polling mode enabled.");
                    await RunLongPollingAsync(stoppingToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Telegram bot.");
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

        public void EnqueueWebhookUpdate(Update update)
        {
            if (_botClient is null)
            {
                _logger.LogWarning("Cannot process Telegram webhook update because the bot client is not initialized yet.");
                return;
            }

            _ = ProcessUpdateAsync(update, _stoppingToken);
        }

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

                var audioPayload = FindAudioPayload(message);
                if (audioPayload is not null)
                {
                    await HandleAudioAsync(message, audioPayload, cancellationToken).ConfigureAwait(false);
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

        private async Task RunLongPollingAsync(CancellationToken stoppingToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var offset = 0;
            var allowedUpdates = new[] { UpdateType.Message };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var updates = await _botClient
                        .GetUpdatesAsync(offset, timeout: 30, allowedUpdates: allowedUpdates, cancellationToken: stoppingToken)
                        .ConfigureAwait(false);

                    foreach (var update in updates)
                    {
                        offset = update.Id + 1;
                        await ProcessUpdateAsync(update, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while polling Telegram updates.");

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
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

            LogEvent("command", message, rawCommand, new { command });
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

        private async Task HandleAudioAsync(Message triggerMessage, AudioPayload audioPayload, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var logCaption = audioPayload.Caption ?? triggerMessage.Caption ?? triggerMessage.Text ?? string.Empty;
            var logExtra = audioPayload.CreateLogContext();

            LogEvent("incoming", triggerMessage, logCaption, logExtra);

            await _botClient.SendChatActionAsync(triggerMessage.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            Message? status = null;
            try
            {
                status = await _botClient.SendTextMessageAsync(
                    chatId: triggerMessage.Chat.Id,
                    text: "⏳ Обрабатываю…",
                    replyParameters: new ReplyParameters { MessageId = triggerMessage.MessageId },
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
                var sourcePath = await DownloadMediaAsync(audioPayload, tempRoot, cancellationToken).ConfigureAwait(false);
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
                    chatId: triggerMessage.Chat.Id,
                    text: header,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var postProcessing = await PostProcessTranscriptAsync(
                    transcript.Text,
                    transcript.Language,
                    cancellationToken).ConfigureAwait(false);

                if (postProcessing.Error is { Length: > 0 } && postProcessing.Attempted && string.IsNullOrWhiteSpace(postProcessing.Model))
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: triggerMessage.Chat.Id,
                        text: $"⚠️ GPT пост-обработка не выполнена: {postProcessing.Error}",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await SendTranscriptAsync(triggerMessage, postProcessing.Text, tempRoot, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(postProcessing.Summary))
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: triggerMessage.Chat.Id,
                        text: "📄 Краткое резюме:\n" + postProcessing.Summary,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                var transcriptLog = MergeLogContexts(logExtra, new Dictionary<string, object?>
                {
                    ["language"] = transcript.Language,
                    ["language_probability"] = transcript.LanguageProbability,
                    ["model"] = _model,
                    ["openai_model"] = postProcessing.Model,
                    ["openai_error"] = postProcessing.Error,
                    ["summary"] = postProcessing.Summary
                });

                LogEvent("transcript", triggerMessage, postProcessing.Text, transcriptLog);
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
                _logger.LogError(ex, "Failed to process audio payload {MessageId}.", audioPayload.SourceMessageId);
                var errorText = $"⚠️ Ошибка: {ex.Message}";
                await EditStatusAsync(status, errorText, cancellationToken).ConfigureAwait(false);
                LogEvent("error", triggerMessage, ex.Message, logExtra);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private async Task<string?> DownloadMediaAsync(AudioPayload payload, string directory, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(payload.FileId) || string.IsNullOrWhiteSpace(payload.FileName))
            {
                return null;
            }

            var file = await _botClient.GetFileAsync(payload.FileId, cancellationToken: cancellationToken).ConfigureAwait(false);
            var destination = Path.Combine(directory, payload.FileName);

            await using var fs = IOFile.Create(destination);
            await _botClient.DownloadFileAsync(file.FilePath!, fs, cancellationToken).ConfigureAwait(false);
            return destination;
        }

        private async Task<string> ConvertToWav16kMonoAsync(string sourcePath, string directory, CancellationToken cancellationToken)
        {
            var overrideExecutable = _optionsMonitor.CurrentValue.FfmpegExecutable;
            if (string.IsNullOrWhiteSpace(overrideExecutable))
            {
                overrideExecutable = _ffmpegExecutable;
            }

            var outputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(sourcePath) + "_16k.wav");
            await _ffmpegService.ConvertToWav16kMonoAsync(sourcePath, outputPath, cancellationToken, overrideExecutable);
            return outputPath;
        }

        private async Task<(string Text, string? Language, double? LanguageProbability)> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
        {
            var overrideExecutable = _optionsMonitor.CurrentValue.FfmpegExecutable ?? _ffmpegExecutable;
            var resolvedFfmpeg = _ffmpegService.ResolveFfmpegExecutable(overrideExecutable);

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
                FfmpegExecutable = resolvedFfmpeg
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

        private async Task<OpenAiPostProcessingResult> PostProcessTranscriptAsync(
            string rawText,
            string? language,
            CancellationToken cancellationToken)
        {
            var text = rawText.Trim();
            var options = _optionsMonitor.CurrentValue;

            if (!options.EnableOpenAiPostProcessing)
            {
                return new OpenAiPostProcessingResult(text, null, null, "OpenAI post-processing is disabled.", false);
            }

            var apiKey = !string.IsNullOrWhiteSpace(options.OpenAiApiKey)
                ? options.OpenAiApiKey
                : _globalOpenAiApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new OpenAiPostProcessingResult(text, null, null, "OpenAI API key is not configured.", false);
            }

            var model = string.IsNullOrWhiteSpace(options.OpenAiModel)
                ? DefaultOpenAiModel
                : options.OpenAiModel!.Trim();

            if (string.IsNullOrWhiteSpace(model))
            {
                model = DefaultOpenAiModel;
            }

            var threshold = options.OpenAiSummaryWordThreshold > 0
                ? options.OpenAiSummaryWordThreshold
                : DefaultSummaryThreshold;

            var needSummary = CountWords(text) > threshold;
            var userMessage = $"LANG_HINT={language ?? "unknown"}\nNEED_SUMMARY={(needSummary ? "yes" : "no")}\n\nRAW_TEXT:\n{text}";

            var payload = new
            {
                model,
                temperature = 0.2,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = OpenAiSystemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            var client = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot) + ".OpenAI");
            client.Timeout = TimeSpan.FromMinutes(2);

            using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, OpenAiRequestJsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var message = $"OpenAI HTTP {(int)response.StatusCode}: {response.ReasonPhrase?.Trim() ?? "Unknown"}. {Truncate(responseBody, 400)}";
                    return new OpenAiPostProcessingResult(text, null, null, message, true);
                }

                using var responseJson = JsonDocument.Parse(responseBody);
                if (!responseJson.RootElement.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                {
                    return new OpenAiPostProcessingResult(text, null, null, "OpenAI response did not contain choices.", true);
                }

                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new OpenAiPostProcessingResult(text, null, null, "OpenAI response content is empty.", true);
                }

                using var payloadJson = TryParseJsonDocument(content!);
                if (payloadJson is null)
                {
                    return new OpenAiPostProcessingResult(text, null, null, "OpenAI response content is not valid JSON.", true);
                }

                var root = payloadJson.RootElement;
                var polished = root.TryGetProperty("polished", out var polishedElement) && polishedElement.ValueKind == JsonValueKind.String
                    ? polishedElement.GetString()
                    : null;

                var finalText = string.IsNullOrWhiteSpace(polished) ? text : polished!.Trim();

                string? summary = null;
                if (needSummary
                    && root.TryGetProperty("summary", out var summaryElement)
                    && summaryElement.ValueKind == JsonValueKind.String)
                {
                    var summaryValue = summaryElement.GetString();
                    if (!string.IsNullOrWhiteSpace(summaryValue))
                    {
                        summary = summaryValue!.Trim();
                    }
                }

                return new OpenAiPostProcessingResult(finalText, summary, model, null, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new OpenAiPostProcessingResult(text, null, null, $"{ex.GetType().Name}: {ex.Message}", true);
            }
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

        private static JsonDocument? TryParseJsonDocument(string content)
        {
            try
            {
                return JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                var start = content.IndexOf('{');
                var end = content.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    var slice = content[start..(end + 1)];
                    try
                    {
                        return JsonDocument.Parse(slice);
                    }
                    catch (JsonException)
                    {
                        // ignore
                    }
                }
            }

            return null;
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return Regex.Matches(text, "\\w+", RegexOptions.CultureInvariant | RegexOptions.Multiline).Count;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            return trimmed[..maxLength] + "…";
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
            var defaultDirectory = Path.Combine("c:", "log");
            var defaultPath = Path.Combine(defaultDirectory, "telegram_messages.log");
            var path = string.IsNullOrWhiteSpace(options.LogFilePath)
                ? defaultPath
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

        private static IReadOnlyDictionary<string, object?> ExtractOriginMetadata(MessageOrigin? origin)
        {
            var metadata = new Dictionary<string, object?>();
            if (origin is null)
            {
                return metadata;
            }

            switch (origin)
            {
                case MessageOriginUser user:
                    metadata["external_reply_origin_type"] = "user";
                    metadata["external_reply_origin_sender_user_id"] = user.SenderUser?.Id;
                    metadata["external_reply_origin_sender_user_username"] = user.SenderUser?.Username;
                    metadata["external_reply_origin_sender_user_full_name"] = BuildFullName(user.SenderUser);
                    metadata["external_reply_origin_date"] = user.Date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    break;
                case MessageOriginHiddenUser hidden:
                    metadata["external_reply_origin_type"] = "hidden_user";
                    metadata["external_reply_origin_sender_user_name"] = hidden.SenderUserName;
                    metadata["external_reply_origin_date"] = hidden.Date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    break;
                case MessageOriginChat chat:
                    metadata["external_reply_origin_type"] = "chat";
                    metadata["external_reply_origin_sender_chat_id"] = chat.SenderChat?.Id;
                    metadata["external_reply_origin_sender_chat_username"] = chat.SenderChat?.Username;
                    metadata["external_reply_origin_sender_chat_title"] = chat.SenderChat?.Title;
                    metadata["external_reply_origin_author_signature"] = chat.AuthorSignature;
                    metadata["external_reply_origin_date"] = chat.Date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    break;
                case MessageOriginChannel channel:
                    metadata["external_reply_origin_type"] = "channel";
                    metadata["external_reply_origin_chat_id"] = channel.Chat.Id;
                    metadata["external_reply_origin_chat_username"] = channel.Chat.Username;
                    metadata["external_reply_origin_chat_title"] = channel.Chat.Title;
                    metadata["external_reply_origin_message_id"] = channel.MessageId;
                    metadata["external_reply_origin_author_signature"] = channel.AuthorSignature;
                    metadata["external_reply_origin_date"] = channel.Date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                    break;
                default:
                    metadata["external_reply_origin_type"] = origin.GetType().Name;
                    break;
            }

            return metadata;
        }

        private AudioPayload? FindAudioPayload(Message message)
        {
            var visitedMessages = new HashSet<int>();
            Message? current = message;

            while (current is not null)
            {
                if (TryCreatePayloadFromMessage(current, out var payload))
                {
                    return payload;
                }

                if (current.ExternalReply is { } externalReply)
                {
                    var externalPayload = TryCreatePayloadFromExternalReply(externalReply);
                    if (externalPayload is not null)
                    {
                        return externalPayload;
                    }
                }

                if (!visitedMessages.Add(current.MessageId))
                {
                    break;
                }

                current = current.ReplyToMessage;
            }

            if (message.ExternalReply is { } messageExternalReply)
            {
                return TryCreatePayloadFromExternalReply(messageExternalReply);
            }

            return null;
        }

        private static bool TryCreatePayloadFromMessage(Message message, out AudioPayload payload)
        {
            var source = new MessagePayloadSource(message.Chat?.Id, message.MessageId);

            if (message.Voice is { } voice)
            {
                payload = CreateVoicePayload(voice.FileId, voice.FileUniqueId, voice.MimeType, message.Caption, source);
                return true;
            }

            if (message.Audio is { } audio)
            {
                payload = CreateAudioPayload(audio.FileId, audio.FileUniqueId, audio.FileName, audio.MimeType, message.Caption, source);
                return true;
            }

            if (message.VideoNote is { } videoNote)
            {
                payload = CreateVideoNotePayload(videoNote.FileId, videoNote.FileUniqueId, message.Caption, source);
                return true;
            }

            if (message.Video is { } video)
            {
                payload = CreateVideoPayload(video.FileId, video.FileUniqueId, video.MimeType, message.Caption, source);
                return true;
            }

            if (message.Document is { } document)
            {
                var documentKind = GetDocumentMediaKind(document.FileName, document.MimeType);
                if (documentKind is not null)
                {
                    payload = CreateDocumentPayload(documentKind.Value, document.FileId, document.FileUniqueId, document.FileName, document.MimeType, message.Caption, source);
                    return true;
                }
            }

            payload = default!;
            return false;
        }

        private AudioPayload? TryCreatePayloadFromExternalReply(ExternalReplyInfo externalReply)
        {
            var originMetadata = ExtractOriginMetadata(externalReply.Origin);
            var source = new ExternalReplyPayloadSource(externalReply.Chat?.Id, externalReply.MessageId, originMetadata);

            if (externalReply.Voice is { } voice)
            {
                return CreateVoicePayload(voice.FileId, voice.FileUniqueId, voice.MimeType, null, source);
            }

            if (externalReply.Audio is { } audio)
            {
                return CreateAudioPayload(audio.FileId, audio.FileUniqueId, audio.FileName, audio.MimeType, null, source);
            }

            if (externalReply.VideoNote is { } videoNote)
            {
                return CreateVideoNotePayload(videoNote.FileId, videoNote.FileUniqueId, null, source);
            }

            if (externalReply.Video is { } video)
            {
                return CreateVideoPayload(video.FileId, video.FileUniqueId, video.MimeType, null, source);
            }

            if (externalReply.Document is { } document)
            {
                var documentKind = GetDocumentMediaKind(document.FileName, document.MimeType);
                if (documentKind is not null)
                {
                    return CreateDocumentPayload(documentKind.Value, document.FileId, document.FileUniqueId, document.FileName, document.MimeType, null, source);
                }
            }

            return null;
        }

        private static AudioPayload CreateVoicePayload(string fileId, string fileUniqueId, string? mimeType, string? caption, PayloadSource source)
        {
            var extension = GetDefaultExtension(MediaPayloadKind.Voice, mimeType);
            var fileName = BuildFileName(null, fileUniqueId, extension);
            return new AudioPayload(MediaPayloadKind.Voice, fileId, fileName, mimeType, caption, source);
        }

        private static AudioPayload CreateAudioPayload(string fileId, string fileUniqueId, string? fileName, string? mimeType, string? caption, PayloadSource source)
        {
            var extension = GetDefaultExtension(MediaPayloadKind.Audio, mimeType);
            var resolvedFileName = BuildFileName(fileName, fileUniqueId, extension);
            return new AudioPayload(MediaPayloadKind.Audio, fileId, resolvedFileName, mimeType, caption, source);
        }

        private static AudioPayload CreateVideoNotePayload(string fileId, string fileUniqueId, string? caption, PayloadSource source)
        {
            var fileName = BuildFileName(null, fileUniqueId, ".mp4");
            return new AudioPayload(MediaPayloadKind.VideoNote, fileId, fileName, "video/mp4", caption, source);
        }

        private static AudioPayload CreateVideoPayload(string fileId, string fileUniqueId, string? mimeType, string? caption, PayloadSource source)
        {
            var extension = GetDefaultExtension(MediaPayloadKind.Video, mimeType);
            var fileName = BuildFileName(null, fileUniqueId, extension);
            return new AudioPayload(MediaPayloadKind.Video, fileId, fileName, mimeType, caption, source);
        }

        private static AudioPayload CreateDocumentPayload(MediaPayloadKind kind, string fileId, string fileUniqueId, string? fileName, string? mimeType, string? caption, PayloadSource source)
        {
            var extension = GetDefaultExtension(kind, mimeType);
            var resolvedFileName = BuildFileName(fileName, fileUniqueId, extension);
            return new AudioPayload(kind, fileId, resolvedFileName, mimeType, caption, source);
        }

        private static MediaPayloadKind? GetDocumentMediaKind(string? fileName, string? mimeType)
        {
            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                if (mimeType.StartsWith("audio", StringComparison.OrdinalIgnoreCase))
                {
                    return MediaPayloadKind.DocumentAudio;
                }

                if (mimeType.StartsWith("video", StringComparison.OrdinalIgnoreCase))
                {
                    return MediaPayloadKind.DocumentVideo;
                }
            }

            var extension = Path.GetExtension(fileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                if (AudioFileExtensions.Contains(extension))
                {
                    return MediaPayloadKind.DocumentAudio;
                }

                if (VideoFileExtensions.Contains(extension))
                {
                    return MediaPayloadKind.DocumentVideo;
                }
            }

            return null;
        }

        private static string BuildFileName(string? providedName, string fallbackStem, string defaultExtension)
        {
            var candidate = string.IsNullOrWhiteSpace(providedName) ? fallbackStem : Path.GetFileName(providedName);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = fallbackStem;
            }

            var sanitized = SanitizeFileName(candidate);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)) && !string.IsNullOrWhiteSpace(defaultExtension))
            {
                sanitized = Path.ChangeExtension(sanitized, defaultExtension);
            }

            return sanitized;
        }

        private static string GetDefaultExtension(MediaPayloadKind kind, string? mimeType)
        {
            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                var normalized = mimeType.ToLowerInvariant();
                if (normalized.Contains("ogg"))
                {
                    return ".ogg";
                }

                if (normalized.Contains("opus"))
                {
                    return ".opus";
                }

                if (normalized.Contains("mpeg") || normalized.Contains("mp3"))
                {
                    return ".mp3";
                }

                if (normalized.Contains("x-m4a") || normalized.Contains("m4a"))
                {
                    return ".m4a";
                }

                if (normalized.Contains("aac"))
                {
                    return ".aac";
                }

                if (normalized.Contains("amr"))
                {
                    return ".amr";
                }

                if (normalized.Contains("flac"))
                {
                    return ".flac";
                }

                if (normalized.Contains("wav"))
                {
                    return ".wav";
                }

                if (normalized.Contains("webm"))
                {
                    return ".webm";
                }

                if (normalized.Contains("mp4"))
                {
                    return ".mp4";
                }

                if (normalized.Contains("3gpp") || normalized.Contains("3gp"))
                {
                    return ".3gp";
                }
            }

            return kind switch
            {
                MediaPayloadKind.Voice => ".oga",
                MediaPayloadKind.Audio => ".mp3",
                MediaPayloadKind.Video => ".mp4",
                MediaPayloadKind.VideoNote => ".mp4",
                MediaPayloadKind.DocumentAudio => ".mp3",
                MediaPayloadKind.DocumentVideo => ".mp4",
                _ => ".bin"
            };
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            var sanitized = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            {
                sanitized = Guid.NewGuid().ToString("N");
            }

            return sanitized;
        }

        private enum MediaPayloadKind
        {
            Voice,
            Audio,
            Video,
            VideoNote,
            DocumentAudio,
            DocumentVideo
        }

        private abstract record PayloadSource(long? ChatIdValue, int? MessageIdValue)
        {
            public long? ChatId { get; } = ChatIdValue;
            public int? MessageId { get; } = MessageIdValue;

            public abstract IReadOnlyDictionary<string, object?> CreateLogContext();
        }

        private sealed record MessagePayloadSource(long? ChatIdValue, int? MessageIdValue)
            : PayloadSource(ChatIdValue, MessageIdValue)
        {
            public override IReadOnlyDictionary<string, object?> CreateLogContext()
            {
                return new Dictionary<string, object?>
                {
                    ["audio_source_origin"] = "message",
                    ["audio_source_message_id"] = MessageId,
                    ["audio_source_chat_id"] = ChatId
                };
            }
        }

        private sealed record ExternalReplyPayloadSource(
            long? ChatIdValue,
            int? MessageIdValue,
            IReadOnlyDictionary<string, object?> OriginMetadata)
            : PayloadSource(ChatIdValue, MessageIdValue)
        {
            public override IReadOnlyDictionary<string, object?> CreateLogContext()
            {
                var context = new Dictionary<string, object?>
                {
                    ["audio_source_origin"] = "external_reply",
                    ["external_reply_message_id"] = MessageId,
                    ["external_reply_chat_id"] = ChatId
                };

                foreach (var pair in OriginMetadata)
                {
                    context[pair.Key] = pair.Value;
                }

                return context;
            }
        }

        private sealed record AudioPayload(
            MediaPayloadKind Kind,
            string FileId,
            string FileName,
            string? MimeType,
            string? Caption,
            PayloadSource Source)
        {
            public Dictionary<string, object?> CreateLogContext()
            {
                var context = new Dictionary<string, object?>
                {
                    ["audio_source_kind"] = Kind.ToString().ToLowerInvariant(),
                    ["audio_file_name"] = FileName,
                    ["audio_file_mime_type"] = MimeType
                };

                foreach (var pair in Source.CreateLogContext())
                {
                    context[pair.Key] = pair.Value;
                }

                return context;
            }

            public long? SourceChatId => Source.ChatId;
            public int? SourceMessageId => Source.MessageId;
        }

        private static Dictionary<string, object?> MergeLogContexts(Dictionary<string, object?> baseContext, Dictionary<string, object?> additional)
        {
            var merged = new Dictionary<string, object?>(baseContext);
            foreach (var pair in additional)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
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
