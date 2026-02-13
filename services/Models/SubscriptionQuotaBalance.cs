using System;
using System.Collections.Generic;

namespace YandexSpeech.services.Models
{
    public sealed class SubscriptionQuotaBalance
    {
        public int TotalTranscriptionMinutes { get; init; }

        public int RemainingTranscriptionMinutes { get; init; }

        public int TotalVideos { get; init; }

        public int RemainingVideos { get; init; }

        public IReadOnlyList<SubscriptionQuotaPackage> Packages { get; init; } = Array.Empty<SubscriptionQuotaPackage>();
    }

    public sealed class SubscriptionQuotaPackage
    {
        public Guid SubscriptionId { get; init; }

        public Guid PlanId { get; init; }

        public string PlanCode { get; init; } = string.Empty;

        public string PlanName { get; init; } = string.Empty;

        public int RemainingTranscriptionMinutes { get; init; }

        public int RemainingVideos { get; init; }

        public DateTime CreatedAt { get; init; }
    }
}
