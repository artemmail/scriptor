using System;

namespace YandexSpeech.services.Options
{
    public sealed class TelegramBotOptions
    {
        public bool Enabled { get; set; } = true;

        public string BotToken { get; set; } = string.Empty;

        public string? ApiBaseUrl { get; set; }

        public string? WebhookUrl { get; set; }

        public string? WebhookSecretToken { get; set; }

        public bool UseLongPolling { get; set; }

        public string? LogFilePath { get; set; }

        public int MessageChunkLimit { get; set; } = 3900;

        public string? FfmpegExecutable { get; set; }

        public void Validate()
        {
            if (!Enabled)
            {
                throw new InvalidOperationException("Telegram bot is disabled.");
            }

            if (string.IsNullOrWhiteSpace(BotToken))
            {
                throw new InvalidOperationException("Telegram bot token is not configured.");
            }
        }
    }
}
