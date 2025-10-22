using System;

namespace YandexSpeech.services.Options
{
    public sealed class TelegramIntegrationOptions
    {
        public string LinkBaseUrl { get; set; } = string.Empty;

        public string? IntegrationApiToken { get; set; }

        public string TokenSigningKey { get; set; } = string.Empty;

        public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

        public int MaxActiveTokensPerLink { get; set; } = 5;

        public TimeSpan StatusCacheDuration { get; set; } = TimeSpan.FromSeconds(30);

        public TimeSpan LinkInactivityTimeout { get; set; } = TimeSpan.FromDays(30);

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(LinkBaseUrl))
            {
                throw new InvalidOperationException("TelegramIntegration:LinkBaseUrl is not configured.");
            }

            if (string.IsNullOrWhiteSpace(TokenSigningKey))
            {
                throw new InvalidOperationException("TelegramIntegration:TokenSigningKey is not configured.");
            }
        }
    }
}
