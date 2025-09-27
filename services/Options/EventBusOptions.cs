using System;

namespace YandexSpeech.services.Options
{
    public sealed class EventBusOptions
    {
        public bool Enabled { get; set; }
        public string Broker { get; set; } = string.Empty;
        public int RetryCount { get; set; } = 3;
        public string QueueName { get; set; } = string.Empty;
        public string CommandQueueName { get; set; } = string.Empty;
        public string ExchangeType { get; set; } = string.Empty;
        public EventBusAccessOptions BusAccess { get; set; } = new();

        public void Validate()
        {
            if (!Enabled)
                throw new InvalidOperationException("Event bus is disabled in configuration.");

            if (string.IsNullOrWhiteSpace(QueueName))
                throw new InvalidOperationException("Event bus QueueName must be configured.");

            if (string.IsNullOrWhiteSpace(CommandQueueName))
                throw new InvalidOperationException("Event bus CommandQueueName must be configured.");
        }
    }

    public sealed class EventBusAccessOptions
    {
        public string Host { get; set; } = "localhost";
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public int RetryCount { get; set; } = 3;
    }
}
