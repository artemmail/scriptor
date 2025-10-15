using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using YandexSpeech.models.DB;

namespace YandexSpeech.models.DTO
{
    public class UsageLimitExceededResponse
    {
        public string Message { get; set; } = string.Empty;

        public string PaymentUrl { get; set; } = "/billing";

        public int? RemainingQuota { get; set; }
    }

    public class SubscriptionPaymentHistoryItemDto
    {
        public Guid InvoiceId { get; set; }

        public string? PlanName { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "RUB";

        public SubscriptionInvoiceStatus Status { get; set; }

        public DateTime IssuedAt { get; set; }

        public DateTime? PaidAt { get; set; }

        public string? PaymentProvider { get; set; }

        public string? ExternalInvoiceId { get; set; }

        public string? Comment { get; set; }
    }

    public class SubscriptionSummaryDto
    {
        public bool HasActiveSubscription { get; set; }

        public bool HasLifetimeAccess { get; set; }

        public string? PlanCode { get; set; }

        public string? PlanName { get; set; }

        public SubscriptionStatus? Status { get; set; }

        public DateTime? EndsAt { get; set; }

        public bool IsLifetime { get; set; }

        public int FreeRecognitionsPerDay { get; set; }

        public int FreeTranscriptionsPerMonth { get; set; }

        public string BillingUrl { get; set; } = "/billing";

        public IReadOnlyList<SubscriptionPaymentHistoryItemDto> Payments { get; set; }
            = Array.Empty<SubscriptionPaymentHistoryItemDto>();
    }

    public class StartSubtitleRecognitionResponse
    {
        public string TaskId { get; set; } = string.Empty;

        public int? RemainingQuota { get; set; }
    }

    public class ManualSubscriptionPaymentRequest
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string PlanCode { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal? Amount { get; set; }

        [MaxLength(8)]
        public string? Currency { get; set; }

        public DateTime? EndDate { get; set; }

        public DateTime? PaidAt { get; set; }

        [MaxLength(128)]
        public string? Reference { get; set; }

        [MaxLength(512)]
        public string? Comment { get; set; }
    }

    public class ExtendSubscriptionRequest
    {
        public DateTime? EndDate { get; set; }
    }

    public class AdminSubscriptionDto
    {
        public Guid Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string? UserEmail { get; set; }

        public string PlanCode { get; set; } = string.Empty;

        public string PlanName { get; set; } = string.Empty;

        public SubscriptionStatus Status { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public bool IsLifetime { get; set; }

        public bool AutoRenew { get; set; }

        public string? ExternalPaymentId { get; set; }
    }
}
