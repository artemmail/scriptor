using Microsoft.Extensions.Options;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Resources;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;
using YandexSpeech.services.Whisper;
using YandexSpeech.services.TelegramTranscriptionBot.State;
using YandexSpeech.models.DTO.Telegram;
using YandexSpeech.services.Authentication;
using IOFile = System.IO.File;
using TGFile = Telegram.Bot.Types.TGFile;

namespace YandexSpeech.services.Telegram
{
    public sealed class TelegramTranscriptionBot : BackgroundService
    {
        private readonly ILogger<TelegramTranscriptionBot> _logger;
        private readonly IOptionsMonitor<TelegramBotOptions> _optionsMonitor;
        private readonly IOptionsMonitor<TelegramIntegrationOptions> _integrationOptions;
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
        private readonly TelegramUserStateStore _userStateStore;
        private readonly JsonSerializerOptions _integrationJsonOptions;
        private readonly TimeSpan _integrationRequestTimeout = TimeSpan.FromSeconds(5);

        private const string DefaultOpenAiModel = "gpt-4.1";
        private const int DefaultSummaryThreshold = 70;
        private const string OpenAiEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string DefaultTelegramApiBaseUrl = "https://api.telegram.org";
        private const string IntegrationApiTokenPlaceholder = "changeme";
        private const string OpenAiSystemPrompt =
            """You are a meticulous editor for Telegram voice transcriptions. Fix punctuation, casing, and obvious ASR mistakes without adding content. Keep the input language. Output JSON with fields: polished, summary.""";

        private static readonly JsonSerializerOptions OpenAiRequestJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly ResourceManager BotResourceManager = new("YandexSpeech.resources.bot", typeof(TelegramTranscriptionBot).Assembly);

        // ⬇️ Расширено: больше реальных аудио-расширений (часто прилетают с octet-stream)
        private static readonly HashSet<string> AudioFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aac",".amr",".flac",".m4a",".m4b",".mp3",".mpga",".oga",".ogg",".opus",
            ".wav",".weba",".wma",".caf"
        };

        private static readonly HashSet<string> VideoFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".3gp",".3gpp",".avi",".m4v",".mkv",".mov",".mp4",".mpeg",".mpg",".webm"
        };

        private CancellationToken _stoppingToken = CancellationToken.None;

        private ITelegramBotClient? _botClient;
        private string? _botToken;
        private string _apiBaseUrl = DefaultTelegramApiBaseUrl;
        private string? _integrationApiBaseUrl;
        private string? _integrationApiToken;
        private TimeSpan _integrationStatusCache = TimeSpan.FromSeconds(30);

        private readonly record struct OpenAiPostProcessingResult(
            string Text,
            string? Summary,
            string? Model,
            string? Error,
            bool Attempted);

        public TelegramTranscriptionBot(
            IOptionsMonitor<TelegramBotOptions> optionsMonitor,
            IOptionsMonitor<TelegramIntegrationOptions> integrationOptions,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            FasterWhisperQueueClient queueClient,
            ILogger<TelegramTranscriptionBot> logger,
            IFfmpegService ffmpegService,
            TelegramUserStateStore userStateStore)
        {
            _optionsMonitor = optionsMonitor;
            _integrationOptions = integrationOptions;
            _httpClientFactory = httpClientFactory;
            _queueClient = queueClient;
            _logger = logger;
            _ffmpegService = ffmpegService;
            _userStateStore = userStateStore;

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
            _integrationJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
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

            _botToken = options.BotToken;
            _apiBaseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                ? DefaultTelegramApiBaseUrl
                : NormalizeApiBaseUrl(options.ApiBaseUrl);
            _integrationApiBaseUrl = NormalizeIntegrationBaseUrl(options.IntegrationApiBaseUrl);
            _integrationApiToken = options.IntegrationApiToken;
            _integrationStatusCache = TimeSpan.FromSeconds(Math.Max(5, options.IntegrationStatusCacheSeconds));

            var botOptions = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                ? new TelegramBotClientOptions(options.BotToken)
                : new TelegramBotClientOptions(options.BotToken, options.ApiBaseUrl);

            var httpClient = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot));
            _botClient = new TelegramBotClient(botOptions, httpClient);

            try
            {
                var me = await GetMeAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Telegram bot connected as @{Username} (id {Id}).", me.Username, me.Id);

                var webhookUrl = options.WebhookUrl;
                var dropPendingUpdates = useLongPolling || string.IsNullOrWhiteSpace(webhookUrl);
                await DeleteWebhookAsync(dropPendingUpdates, stoppingToken)
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
                    await SetWebhookAsync(
                            webhookUrl!,
                            new[] { UpdateType.Message },
                            string.IsNullOrWhiteSpace(options.WebhookSecretToken) ? null : options.WebhookSecretToken,
                            stoppingToken)
                        .ConfigureAwait(false);

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
                var userState = TryGetUserState(message.From);
                var culture = ResolveUserCulture(message.From);

                if (message.Text is string text)
                {
                    if (text.StartsWith("/"))
                    {
                        await HandleCommandAsync(message, text, userState, culture, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    LogEvent("text", message, text, extra: null);
                }

                var audioPayload = FindAudioPayload(message);
                if (audioPayload is not null)
                {
                    await HandleAudioAsync(message, audioPayload, userState, cancellationToken).ConfigureAwait(false);
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

        private TelegramUserState? TryGetUserState(User? user)
        {
            if (user is null)
            {
                return null;
            }

            return _userStateStore.GetOrCreate(user.Id);
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
                    var updates = await GetUpdatesAsync(offset, 30, allowedUpdates, stoppingToken)
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

        private async Task HandleCommandAsync(Message message, string text, TelegramUserState? userState, CultureInfo culture, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var segments = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var rawCommand = segments.FirstOrDefault() ?? text;
            var command = rawCommand.Split('@')[0];
            var args = segments.Skip(1).ToArray();

            LogEvent("command", message, rawCommand, new { command });
            switch (command)
            {
                case "/start":
                    await HandleStartCommandAsync(message, userState, culture, cancellationToken).ConfigureAwait(false);
                    break;
                case "/link":
                    await HandleLinkCommandAsync(message, userState, culture, cancellationToken).ConfigureAwait(false);
                    break;
                case "/model":
                    var info = $"faster-whisper: {_model} | device: {_device} | dtype: {NormalizeCt2(_device, _computeType)}\nVAD: off";
                    await SendTextMessageAsync(
                            message.Chat.Id,
                            info,
                            new ReplyParameters { MessageId = message.Id },
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "/calendar":
                    await HandleCalendarCommandAsync(message, userState, culture, args, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private async Task HandleStartCommandAsync(Message message, TelegramUserState? userState, CultureInfo culture, CancellationToken cancellationToken)
        {
            if (message.From is null)
            {
                return;
            }

            var reply = new ReplyParameters { MessageId = message.Id };

            if (!TryGetIntegrationConfiguration(out var baseUrl, out var token, out var errorResourceKey))
            {
                var errorMessage = GetBotString(errorResourceKey ?? "TelegramLinkError", culture);
                await SendTextMessageAsync(message.Chat.Id, errorMessage, reply, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var status = await EnsureIntegrationStatusAsync(
                        message.From,
                        userState,
                        forceRefresh: false,
                        baseUrl!,
                        token!,
                        cancellationToken)
                    .ConfigureAwait(false);

                Uri? linkUrl = null;
                if (status == null || !status.Linked || status.State is TelegramIntegrationStates.Pending or TelegramIntegrationStates.Revoked)
                {
                    var linkResponse = await RequestLinkTokenFromApiAsync(message.From, baseUrl!, token!, cancellationToken).ConfigureAwait(false);
                    if (linkResponse?.Status is not null)
                    {
                        status = linkResponse.Status;
                        UpdateIntegrationState(message.From.Id, userState, status);
                    }
                    linkUrl = linkResponse?.LinkUrl;
                }

                if (status is null)
                {
                    var fallback = GetBotString("TelegramLinkError", culture);
                    await SendTextMessageAsync(message.Chat.Id, fallback, reply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var messageText = BuildIntegrationStatusMessage(status, culture, linkUrl?.ToString());
                await SendTextMessageAsync(message.Chat.Id, messageText, reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorDetails = BuildIntegrationExceptionMessage(ex);
                await SendTextMessageAsync(message.Chat.Id, errorDetails, reply, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleLinkCommandAsync(Message message, TelegramUserState? userState, CultureInfo culture, CancellationToken cancellationToken)
        {
            if (message.From is null)
            {
                return;
            }

            var reply = new ReplyParameters { MessageId = message.Id };

            if (!TryGetIntegrationConfiguration(out var baseUrl, out var token, out var errorResourceKey))
            {
                var errorMessage = GetBotString(errorResourceKey ?? "TelegramLinkError", culture);
                await SendTextMessageAsync(message.Chat.Id, errorMessage, reply, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var linkResponse = await RequestLinkTokenFromApiAsync(message.From, baseUrl!, token!, cancellationToken).ConfigureAwait(false);
                if (linkResponse?.LinkUrl is null)
                {
                    var error = GetBotString("TelegramLinkError", culture);
                    await SendTextMessageAsync(message.Chat.Id, error, reply, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (linkResponse.Status is not null)
                {
                    UpdateIntegrationState(message.From.Id, userState, linkResponse.Status);
                }

                var promptTemplate = GetBotString("TelegramLinkPrompt", culture);
                var text = FormatCalendarLinkMessage(promptTemplate, culture, linkResponse.LinkUrl.ToString());
                await SendTextMessageAsync(message.Chat.Id, text, reply, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorDetails = BuildIntegrationExceptionMessage(ex);
                await SendTextMessageAsync(message.Chat.Id, errorDetails, reply, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleCalendarCommandAsync(Message message, TelegramUserState? userState, CultureInfo culture, string[] args, CancellationToken cancellationToken)
        {
            if (message.From is null)
            {
                return;
            }

            var replyParameters = new ReplyParameters { MessageId = message.Id };
            var refresh = args.Any(a => string.Equals(a, "refresh", StringComparison.OrdinalIgnoreCase));

            if (!TryGetIntegrationConfiguration(out var baseUrl, out var token, out var errorResourceKey))
            {
                var errorMessage = GetBotString(errorResourceKey ?? "TelegramLinkError", culture);
                await SendTextMessageAsync(message.Chat.Id, errorMessage, replyParameters, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var status = await EnsureIntegrationStatusAsync(message.From, userState, refresh, baseUrl!, token!, cancellationToken).ConfigureAwait(false);

                if (status == null)
                {
                    var error = GetBotString("TelegramLinkError", culture);
                    await SendTextMessageAsync(message.Chat.Id, error, replyParameters, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!status.Linked)
                {
                    if (userState is not null)
                    {
                        userState.CalendarScenarioRequested = false;
                    }
                    var linkResponse = await RequestLinkTokenFromApiAsync(message.From!, baseUrl!, token!, cancellationToken).ConfigureAwait(false);
                    var linkUrl = linkResponse?.LinkUrl?.ToString();
                    if (linkResponse?.Status is not null)
                    {
                        status = linkResponse.Status;
                    }

                    var template = GetBotString("TelegramLinkPrompt", culture);
                    var messageText = FormatCalendarLinkMessage(template, culture, linkUrl ?? GetCalendarConsentUrl());
                    await SendTextMessageAsync(message.Chat.Id, messageText, replyParameters, cancellationToken).ConfigureAwait(false);
                    LogEvent("calendar_consent_required", message, messageText, new { stage = "command" });
                    return;
                }

                if (!status.GoogleAuthorized || !status.HasRequiredScope || status.AccessTokenExpired)
                {
                    if (userState is not null)
                    {
                        userState.CalendarScenarioRequested = false;
                    }
                    string templateKey = status.GoogleAuthorized
                        ? status.AccessTokenExpired
                            ? "TelegramLinkTokenExpired"
                            : "TelegramLinkScopeInsufficient"
                        : "CalendarAuthorizationRequired";
                    var template = GetBotString(templateKey, culture);
                    var messageText = FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());
                    await SendTextMessageAsync(message.Chat.Id, messageText, replyParameters, cancellationToken).ConfigureAwait(false);
                    LogEvent("calendar_consent_required", message, messageText, new { stage = "command", detail = status.DetailCode });
                    return;
                }

                if (userState is not null)
                {
                    userState.CalendarScenarioRequested = true;
                }

                var prompt = GetBotString("CalendarScenarioPrompt", culture);
                await SendTextMessageAsync(message.Chat.Id, prompt, replyParameters, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorDetails = BuildIntegrationExceptionMessage(ex);
                await SendTextMessageAsync(message.Chat.Id, errorDetails, replyParameters, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<TelegramCalendarStatusDto?> EnsureIntegrationStatusAsync(
            User user,
            TelegramUserState? userState,
            bool forceRefresh,
            string baseUrl,
            string token,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (!forceRefresh && userState is not null)
            {
                var age = now - userState.StatusFetchedAt;
                if (age <= _integrationStatusCache && userState.CalendarStatus is not null)
                {
                    return userState.CalendarStatus;
                }
            }

            var status = await FetchCalendarStatusFromApiAsync(user.Id, forceRefresh, baseUrl, token, cancellationToken).ConfigureAwait(false);
            if (status != null)
            {
                UpdateIntegrationState(user.Id, userState, status);
                return status;
            }

            return userState?.CalendarStatus;
        }

        private async Task<TelegramCalendarStatusDto?> FetchCalendarStatusFromApiAsync(
            long telegramId,
            bool refresh,
            string baseUrl,
            string token,
            CancellationToken cancellationToken)
        {
            var endpoint = refresh
                ? $"{baseUrl.TrimEnd('/')}/{telegramId}/calendar-status/refresh"
                : $"{baseUrl.TrimEnd('/')}/{telegramId}/calendar-status";

            using var request = new HttpRequestMessage(refresh ? HttpMethod.Post : HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation(IntegrationApiAuthenticationDefaults.HeaderName, token);

            if (refresh)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            try
            {
                using var response = await SendIntegrationRequestAsync(request, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Integration status request for {TelegramId} failed with {StatusCode}.", telegramId, response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var envelope = await JsonSerializer.DeserializeAsync<TelegramCalendarStatusResponse>(stream, _integrationJsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
                return envelope?.Status;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to request Telegram integration status for {TelegramId}.", telegramId);
                throw;
            }

            return null;
        }

        private async Task<TelegramLinkInitiateResponse?> RequestLinkTokenFromApiAsync(
            User user,
            string baseUrl,
            string token,
            CancellationToken cancellationToken)
        {
            var endpoint = $"{baseUrl.TrimEnd('/')}/link/initiate";
            var payload = new TelegramLinkInitiateRequest
            {
                TelegramId = user.Id,
                Username = user.Username,
                FirstName = user.FirstName,
                LastName = user.LastName,
                LanguageCode = user.LanguageCode
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _integrationJsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(IntegrationApiAuthenticationDefaults.HeaderName, token);

            try
            {
                using var response = await SendIntegrationRequestAsync(request, cancellationToken).ConfigureAwait(false);
                if (response is null)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Telegram link initiation failed with {StatusCode} for {TelegramId}.", response.StatusCode, user.Id);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<TelegramLinkInitiateResponse>(stream, _integrationJsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to initiate Telegram link for {TelegramId}.", user.Id);
                throw;
            }

            return null;
        }

        private async Task<HttpResponseMessage?> SendIntegrationRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot) + ".Integration");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_integrationRequestTimeout);

            try
            {
                return await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Integration API request to {Url} timed out after {Timeout}.", request.RequestUri, _integrationRequestTimeout);
            }

            return null;
        }

        private void UpdateIntegrationState(long telegramId, TelegramUserState? userState, TelegramCalendarStatusDto status)
        {
            var now = DateTime.UtcNow;
            _userStateStore.UpdateCalendarStatus(telegramId, status, now);
            if (userState is not null)
            {
                userState.CalendarStatus = status;
                userState.StatusFetchedAt = now;
                userState.HasCalendarConsent = status.HasCalendarAccess;
            }
        }

        private string BuildIntegrationStatusMessage(TelegramCalendarStatusDto status, CultureInfo culture, string? linkUrl)
        {
            if (status.HasCalendarAccess)
            {
                var template = GetBotString("CalendarScenarioPrompt", culture);
                return string.IsNullOrWhiteSpace(template)
                    ? "✅ Доступ к Google Calendar есть. Отправьте голосовое или текстовое описание события — добавлю встречу в календарь."
                    : template;
            }

            if (!status.Linked || status.State is TelegramIntegrationStates.Pending or TelegramIntegrationStates.Revoked)
            {
                var template = GetBotString("TelegramLinkPrompt", culture);
                var url = linkUrl ?? GetCalendarConsentUrl();
                return FormatCalendarLinkMessage(template, culture, url);
            }

            if (!status.GoogleAuthorized)
            {
                var template = GetBotString("CalendarAuthorizationRequired", culture);
                return FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());
            }

            if (status.AccessTokenExpired)
            {
                var template = GetBotString("TelegramLinkTokenExpired", culture);
                return FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());
            }

            if (!status.HasRequiredScope)
            {
                var template = GetBotString("TelegramLinkScopeInsufficient", culture);
                return FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());
            }

            if (string.Equals(status.DetailCode, TelegramIntegrationDetails.GoogleRevoked, StringComparison.Ordinal))
            {
                var template = GetBotString("TelegramLinkRevoked", culture);
                return FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());
            }

            return GetBotString("TelegramLinkError", culture);
        }

        private async Task HandleAudioAsync(Message triggerMessage, AudioPayload audioPayload, TelegramUserState? userState, CancellationToken cancellationToken)
        {
            if (_botClient is null)
            {
                return;
            }

            var logCaption = audioPayload.Caption ?? triggerMessage.Caption ?? triggerMessage.Text ?? string.Empty;
            var logExtra = audioPayload.CreateLogContext();

            LogEvent("incoming", triggerMessage, logCaption, logExtra);

            await SendChatActionAsync(triggerMessage.Chat.Id, ChatAction.Typing, cancellationToken)
                .ConfigureAwait(false);

            Message? status = null;
            try
            {
                status = await SendTextMessageAsync(
                        triggerMessage.Chat.Id,
                        "⏳ Обрабатываю…",
                        new ReplyParameters { MessageId = triggerMessage.Id },
                        cancellationToken)
                    .ConfigureAwait(false);
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

                await SendTextMessageAsync(triggerMessage.Chat.Id, header, replyParameters: null, cancellationToken)
                    .ConfigureAwait(false);

                var postProcessing = await PostProcessTranscriptAsync(
                    transcript.Text,
                    transcript.Language,
                    cancellationToken).ConfigureAwait(false);

                if (postProcessing.Error is { Length: > 0 } && postProcessing.Attempted && string.IsNullOrWhiteSpace(postProcessing.Model))
                {
                    await SendTextMessageAsync(
                            triggerMessage.Chat.Id,
                            $"⚠️ GPT пост-обработка не выполнена: {postProcessing.Error}",
                            replyParameters: null,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await SendTranscriptAsync(triggerMessage, postProcessing.Text, tempRoot, cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(postProcessing.Summary))
                {
                    await SendTextMessageAsync(
                            triggerMessage.Chat.Id,
                            "📄 Краткое резюме:\n" + postProcessing.Summary,
                            replyParameters: null,
                            cancellationToken)
                        .ConfigureAwait(false);
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

                if (userState is not null)
                {
                    await RunCalendarScenarioIfNeededAsync(triggerMessage, postProcessing, userState, cancellationToken)
                        .ConfigureAwait(false);
                }
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
                var errorHint = "⚠️ Ошибка обработки. Возможно, это не аудио/видео или формат не поддерживается.";
                var errorText = $"{errorHint}\n{ex.Message}";
                await EditStatusAsync(status, errorText, cancellationToken).ConfigureAwait(false);
                LogEvent("error", triggerMessage, ex.Message, logExtra);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private async Task RunCalendarScenarioIfNeededAsync(
            Message triggerMessage,
            OpenAiPostProcessingResult _,
            TelegramUserState userState,
            CancellationToken cancellationToken)
        {
            if (!userState.CalendarScenarioRequested)
            {
                return;
            }

            var culture = ResolveUserCulture(triggerMessage.From);

            if (!userState.HasCalendarConsent)
            {
                var template = GetBotString("CalendarAuthorizationMissingDuringScenario", culture);
                var text = FormatCalendarLinkMessage(template, culture, GetCalendarConsentUrl());

                await SendTextMessageAsync(
                        triggerMessage.Chat.Id,
                        text,
                        new ReplyParameters { MessageId = triggerMessage.Id },
                        cancellationToken)
                    .ConfigureAwait(false);

                LogEvent("calendar_consent_required", triggerMessage, text, new { stage = "scenario" });
                userState.CalendarScenarioRequested = false;
                return;
            }

            // Здесь позже будет логика создания события в календаре.
            userState.CalendarScenarioRequested = false;
        }

        private static CultureInfo ResolveUserCulture(User? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.LanguageCode))
            {
                var languageCode = user.LanguageCode;
                try
                {
                    return CultureInfo.GetCultureInfo(languageCode);
                }
                catch (CultureNotFoundException)
                {
                    try
                    {
                        return new CultureInfo(languageCode);
                    }
                    catch (CultureNotFoundException)
                    {
                        // ignore and fallback below
                    }
                }
            }

            try
            {
                return CultureInfo.GetCultureInfo("ru-RU");
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private string GetCalendarConsentUrl()
        {
            var options = _optionsMonitor.CurrentValue;
            if (!string.IsNullOrWhiteSpace(options.CalendarConsentUrl))
            {
                return options.CalendarConsentUrl!;
            }

            if (!string.IsNullOrWhiteSpace(options.WebhookUrl)
                && Uri.TryCreate(options.WebhookUrl, UriKind.Absolute, out var webhookUri))
            {
                try
                {
                    var builder = new UriBuilder
                    {
                        Scheme = webhookUri.Scheme,
                        Host = webhookUri.Host,
                        Port = webhookUri.IsDefaultPort ? -1 : webhookUri.Port,
                        Path = "profile"
                    };

                    return builder.Uri.ToString();
                }
                catch
                {
                    // ignore and fallback below
                }
            }

            return "https://teamlogs.ru/profile";
        }

        private bool TryGetIntegrationConfiguration(out string? baseUrl, out string? token, out string? errorResourceKey)
        {
            var botOptions = _optionsMonitor.CurrentValue;

            baseUrl = ResolveIntegrationApiBaseUrl(botOptions);
            token = ResolveIntegrationApiToken(botOptions);

            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning(
                    "Telegram integration API configuration is missing. BaseUrl: {BaseUrl}, TokenConfigured: {HasToken}",
                    baseUrl,
                    !string.IsNullOrWhiteSpace(token));
                errorResourceKey = "TelegramLinkError";
                return false;
            }

            if (IsPlaceholderIntegrationToken(token))
            {
                _logger.LogWarning(
                    "Telegram integration API token is using the placeholder value. Update Telegram:IntegrationApiToken configuration.");
                errorResourceKey = "TelegramIntegrationMisconfigured";
                return false;
            }

            errorResourceKey = null;
            return true;
        }

        private string? ResolveIntegrationApiBaseUrl(TelegramBotOptions options)
        {
            var configured = _integrationApiBaseUrl ?? NormalizeIntegrationBaseUrl(options.IntegrationApiBaseUrl);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var fromWebhook = NormalizeIntegrationBaseUrl(DeriveIntegrationApiBaseUrlFromWebhook(options.WebhookUrl));
            if (!string.IsNullOrWhiteSpace(fromWebhook))
            {
                _integrationApiBaseUrl = fromWebhook;
                _logger.LogDebug("Derived Telegram integration API base URL from webhook URL: {BaseUrl}.", fromWebhook);
                return fromWebhook;
            }

            var integrationOptions = _integrationOptions.CurrentValue;
            var fromLink = NormalizeIntegrationBaseUrl(DeriveIntegrationApiBaseUrlFromLink(integrationOptions.LinkBaseUrl));
            if (!string.IsNullOrWhiteSpace(fromLink))
            {
                _integrationApiBaseUrl = fromLink;
                _logger.LogDebug("Derived Telegram integration API base URL from link base URL: {BaseUrl}.", fromLink);
                return fromLink;
            }

            return null;
        }

        private string? ResolveIntegrationApiToken(TelegramBotOptions options)
        {
            var configured = _integrationApiToken ?? options.IntegrationApiToken;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            var integrationToken = _integrationOptions.CurrentValue.IntegrationApiToken;
            if (!string.IsNullOrWhiteSpace(integrationToken))
            {
                var trimmed = integrationToken.Trim();
                _integrationApiToken = trimmed;
                _logger.LogDebug("Using TelegramIntegration options token for Telegram bot integration API calls.");
                return trimmed;
            }

            return null;
        }

        private static bool IsPlaceholderIntegrationToken(string token) =>
            string.Equals(token, IntegrationApiTokenPlaceholder, StringComparison.OrdinalIgnoreCase);

        private static string? NormalizeIntegrationBaseUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return null;
            }

            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                ? uri.ToString().TrimEnd('/')
                : null;
        }

        private static string? DeriveIntegrationApiBaseUrlFromWebhook(string? webhookUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)
                || !Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri))
            {
                return null;
            }

            var segments = webhookUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !string.Equals(segments[^1], "webhook", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var builder = new UriBuilder(webhookUri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

            if (segments.Length == 1)
            {
                builder.Path = "/";
            }
            else
            {
                builder.Path = "/" + string.Join('/', segments[..^1]);
            }

            return builder.Uri.ToString();
        }

        private static string? DeriveIntegrationApiBaseUrlFromLink(string? linkBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(linkBaseUrl)
                || !Uri.TryCreate(linkBaseUrl, UriKind.Absolute, out var linkUri))
            {
                return null;
            }

            var builder = new UriBuilder(linkUri)
            {
                Path = "/api/telegram",
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString();
        }

        private string GetBotString(string key, CultureInfo culture)
        {
            try
            {
                var value = BotResourceManager.GetString(key, culture);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch (MissingManifestResourceException)
            {
                // ignore
            }

            try
            {
                var fallback = BotResourceManager.GetString(key, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(fallback))
                {
                    return fallback;
                }
            }
            catch (MissingManifestResourceException)
            {
                // ignore
            }

            return key switch
            {
                "CalendarAuthorizationRequired" => "🔐 Подключите Google Calendar в профиле: {0}",
                "CalendarScenarioPrompt" => "✅ Доступ к Google Calendar есть. Отправьте голосовое или текстовое описание события — добавлю встречу в календарь.",
                "CalendarAuthorizationMissingDuringScenario" => "⚠️ Не могу добавить событие: нет доступа к Google Calendar. Откройте профиль и подтвердите интеграцию: {0}",
                "TelegramLinkPrompt" => "⚠️ Привяжите Telegram к профилю: {0}",
                "TelegramLinkError" => "❌ Не удалось получить статус интеграции. Попробуйте позже.",
                "TelegramLinkTokenExpired" => "⚠️ Доступ к Google Calendar истёк. Обновите разрешения в профиле: {0}",
                "TelegramLinkScopeInsufficient" => "⚠️ Требуются дополнительные права Google Calendar. Подтвердите доступ в профиле: {0}",
                "TelegramLinkRevoked" => "⚠️ Доступ к Google Calendar был отозван. Подключите интеграцию заново: {0}",
                "TelegramIntegrationMisconfigured" => "⚠️ Интеграция с календарём не настроена. Проверьте токен интеграции в настройках.",
                _ => key
            };
        }

        private string FormatCalendarLinkMessage(string template, CultureInfo culture, string calendarUrl)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                template = "🔐 Подключите Google Calendar в профиле: {0}";
            }

            return template.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(culture, template, calendarUrl)
                : string.Join(' ', template.Trim(), calendarUrl).Trim();
        }

        private static string BuildIntegrationExceptionMessage(Exception exception)
        {
            var builder = new StringBuilder();
            builder.AppendLine("❌ Ошибка интеграции:");
            builder.AppendLine(exception.ToString());
            return builder.ToString();
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

            var file = await GetFileAsync(payload.FileId, cancellationToken).ConfigureAwait(false);
            var destination = Path.Combine(directory, payload.FileName);

            await using var fs = IOFile.Create(destination);
            await DownloadFileAsync(file, fs, cancellationToken).ConfigureAwait(false);
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
                await SendTextMessageAsync(originalMessage.Chat.Id, "Пустой результат.", null, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (text.Length <= limit)
            {
                await SendTextMessageAsync(originalMessage.Chat.Id, text, null, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var fileName = $"transcript_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.txt";
            var filePath = Path.Combine(workingDirectory, fileName);
            await IOFile.WriteAllTextAsync(filePath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            await using var stream = IOFile.OpenRead(filePath);
            await SendDocumentAsync(
                    originalMessage.Chat.Id,
                    InputFile.FromStream(stream, fileName),
                    "🧾 Текст не поместился — отправляю .txt",
                    cancellationToken)
                .ConfigureAwait(false);
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

        private Task<User> GetMeAsync(CancellationToken cancellationToken)
        {
            return RequireClient().GetMe(cancellationToken);
        }

        private async Task DeleteWebhookAsync(bool dropPendingUpdates, CancellationToken cancellationToken)
        {
            await RequireClient()
                .DeleteWebhook(dropPendingUpdates: dropPendingUpdates, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task SetWebhookAsync(string url, IEnumerable<UpdateType>? allowedUpdates, string? secretToken, CancellationToken cancellationToken)
        {
            await RequireClient()
                .SetWebhook(
                    url: url,
                    allowedUpdates: allowedUpdates,
                    secretToken: secretToken,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private Task<Update[]> GetUpdatesAsync(int offset, int timeout, IEnumerable<UpdateType>? allowedUpdates, CancellationToken cancellationToken)
        {
            return RequireClient().GetUpdates(
                offset: offset,
                timeout: timeout,
                allowedUpdates: allowedUpdates,
                cancellationToken: cancellationToken);
        }

        private Task<Message> SendTextMessageAsync(ChatId chatId, string text, ReplyParameters? replyParameters, CancellationToken cancellationToken)
        {
            return RequireClient().SendMessage(
                chatId: chatId,
                text: text,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
        }

        private async Task SendChatActionAsync(ChatId chatId, ChatAction chatAction, CancellationToken cancellationToken)
        {
            await RequireClient()
                .SendChatAction(chatId, action: chatAction, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task DeleteMessageAsync(ChatId chatId, int messageId, CancellationToken cancellationToken)
        {
            await RequireClient()
                .DeleteMessage(chatId, messageId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private Task<Message> SendDocumentAsync(ChatId chatId, InputFile document, string? caption, CancellationToken cancellationToken)
        {
            return RequireClient().SendDocument(
                chatId: chatId,
                document: document,
                caption: caption,
                cancellationToken: cancellationToken);
        }

        private Task<Message> EditMessageTextAsync(ChatId chatId, int messageId, string text, CancellationToken cancellationToken)
        {
            return RequireClient().EditMessageText(
                chatId: chatId,
                messageId: messageId,
                text: text,
                cancellationToken: cancellationToken);
        }

        private async Task<TGFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
        {
            var file = await RequireClient().GetFile(fileId, cancellationToken).ConfigureAwait(false);
            return file ?? throw new InvalidOperationException("Telegram GetFile request returned null.");
        }

        private async Task DownloadFileAsync(TGFile file, Stream destination, CancellationToken cancellationToken)
        {
            var filePath = file?.FilePath ?? throw new InvalidOperationException("Telegram response did not include a file path.");

            var token = _botToken ?? throw new InvalidOperationException("Telegram bot token is not available.");
            var baseUrl = _apiBaseUrl.EndsWith("/file", StringComparison.OrdinalIgnoreCase)
                ? _apiBaseUrl
                : _apiBaseUrl + "/file";
            var requestUri = $"{baseUrl}/bot{token}/{filePath}";

            var client = _httpClientFactory.CreateClient(nameof(TelegramTranscriptionBot) + ".Files");
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private ITelegramBotClient RequireClient()
        {
            return _botClient ?? throw new InvalidOperationException("Telegram bot client is not initialized.");
        }

        private static string NormalizeApiBaseUrl(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return DefaultTelegramApiBaseUrl;
            }

            var withoutSlash = trimmed.TrimEnd('/');
            return withoutSlash.Length == 0 ? DefaultTelegramApiBaseUrl : withoutSlash;
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
                await EditMessageTextAsync(status.Chat.Id, status.Id, text, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to edit Telegram status message {MessageId}.", status.Id);
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
                await DeleteMessageAsync(status.Chat.Id, status.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to delete Telegram status message {MessageId}.", status.Id);
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

                if (!visitedMessages.Add(current.Id))
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
            var source = new MessagePayloadSource(message.Chat?.Id, message.Id);

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

                // ⬇️ Фолбэк: octet-stream без расширения — пробуем как аудио
                if (IsOctetStream(document.MimeType))
                {
                    payload = CreateDocumentPayload(MediaPayloadKind.DocumentAudio, document.FileId, document.FileUniqueId, document.FileName, document.MimeType, message.Caption, source);
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

                // ⬇️ Фолбэк: octet-stream без расширения — пробуем как аудио
                if (IsOctetStream(document.MimeType))
                {
                    return CreateDocumentPayload(MediaPayloadKind.DocumentAudio, document.FileId, document.FileUniqueId, document.FileName, document.MimeType, null, source);
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

        // ⬇️ Новый помощник: распознаём application/octet-stream
        private static bool IsOctetStream(string? mimeType) =>
            string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);

        // ⬇️ Устойчивое определение медиа-типа документа
        private static MediaPayloadKind? GetDocumentMediaKind(string? fileName, string? mimeType)
        {
            // 1) Явные audio/* или video/* — принимаем сразу
            if (!string.IsNullOrWhiteSpace(mimeType) && !IsOctetStream(mimeType))
            {
                if (mimeType.StartsWith("audio", StringComparison.OrdinalIgnoreCase))
                    return MediaPayloadKind.DocumentAudio;

                if (mimeType.StartsWith("video", StringComparison.OrdinalIgnoreCase))
                    return MediaPayloadKind.DocumentVideo;
            }

            // 2) Для octet-stream (и любых странных mime) — проверяем расширение
            var extension = Path.GetExtension(fileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                if (AudioFileExtensions.Contains(extension))
                    return MediaPayloadKind.DocumentAudio;

                if (VideoFileExtensions.Contains(extension))
                    return MediaPayloadKind.DocumentVideo;
            }

            // 3) Не удалось определить
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
                if (normalized.Contains("ogg")) return ".ogg";
                if (normalized.Contains("opus")) return ".opus";
                if (normalized.Contains("mpeg") || normalized.Contains("mp3")) return ".mp3";
                if (normalized.Contains("x-m4a") || normalized.Contains("m4a")) return ".m4a";
                if (normalized.Contains("aac")) return ".aac";
                if (normalized.Contains("amr")) return ".amr";
                if (normalized.Contains("flac")) return ".flac";
                if (normalized.Contains("wav")) return ".wav";
                if (normalized.Contains("webm")) return ".webm";
                if (normalized.Contains("mp4")) return ".mp4";
                if (normalized.Contains("3gpp") || normalized.Contains("3gp")) return ".3gp";
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
