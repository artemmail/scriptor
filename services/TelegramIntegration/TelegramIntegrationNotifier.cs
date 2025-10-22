using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DTO.Telegram;
using YandexSpeech.services.Options;

namespace YandexSpeech.services.TelegramIntegration
{
    public sealed class TelegramIntegrationNotifier : ITelegramIntegrationNotifier
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<TelegramBotOptions> _botOptionsMonitor;
        private readonly ILogger<TelegramIntegrationNotifier> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public TelegramIntegrationNotifier(
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<TelegramBotOptions> botOptionsMonitor,
            ILogger<TelegramIntegrationNotifier> logger)
        {
            _httpClientFactory = httpClientFactory;
            _botOptionsMonitor = botOptionsMonitor;
            _logger = logger;
        }

        public Task NotifyStatusAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default)
        {
            var message = BuildStatusMessage(status);
            return SendAsync(telegramId, message, cancellationToken);
        }

        public Task NotifyLinkCompletedAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default)
        {
            var message = status.HasCalendarAccess
                ? "✅ Привязка выполнена. Google Calendar подключён."
                : "⚠️ Привязка выполнена, но требуется выдать доступ к Google Calendar.";
            return SendAsync(telegramId, message, cancellationToken);
        }

        public Task NotifyLinkFailedAsync(long telegramId, string reason, CancellationToken cancellationToken = default)
        {
            var message = string.IsNullOrWhiteSpace(reason)
                ? "❌ Не удалось выполнить привязку. Попробуйте ещё раз."
                : $"❌ Не удалось выполнить привязку: {reason}";
            return SendAsync(telegramId, message, cancellationToken);
        }

        private async Task SendAsync(long telegramId, string message, CancellationToken cancellationToken)
        {
            var options = _botOptionsMonitor.CurrentValue;
            if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
            {
                _logger.LogDebug("Skipping Telegram notification for {TelegramId} because the bot is disabled.", telegramId);
                return;
            }

            var apiBase = string.IsNullOrWhiteSpace(options.ApiBaseUrl)
                ? "https://api.telegram.org"
                : options.ApiBaseUrl.TrimEnd('/');
            var requestUri = $"{apiBase}/bot{options.BotToken}/sendMessage";

            var payload = new
            {
                chat_id = telegramId,
                text = message,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var httpClient = _httpClientFactory.CreateClient(nameof(TelegramIntegrationNotifier));

            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to send Telegram notification to {TelegramId}. Status {StatusCode}.", telegramId, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogError(ex, "Failed to send Telegram notification to {TelegramId}.", telegramId);
            }
        }

        private static string BuildStatusMessage(TelegramCalendarStatusDto status)
        {
            return status switch
            {
                { HasCalendarAccess: true } => "✅ Привязка и доступ к Google Calendar активны.",
                { Linked: true, GoogleAuthorized: false } => "⚠️ Привязка выполнена, но требуется авторизовать Google Calendar.",
                { Linked: true, AccessTokenExpired: true } => "⚠️ Срок действия токена Google истёк. Нужна повторная авторизация.",
                _ => "⚠️ Telegram не привязан. Используйте /link, чтобы начать." 
            };
        }
    }
}
