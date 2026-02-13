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

        public int? RemainingTranscriptionMinutes { get; set; }

        public int? RemainingVideos { get; set; }

        public int? RequestedTranscriptionMinutes { get; set; }

        public int? MaxUploadMinutes { get; set; }

        public IReadOnlyList<string>? RecognizedTitles { get; set; }
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

        public int FreeTranscriptionMinutes { get; set; }

        public int FreeVideos { get; set; }

        public int RemainingTranscriptionMinutes { get; set; }

        public int RemainingVideos { get; set; }

        public int TotalTranscriptionMinutes { get; set; }

        public int TotalVideos { get; set; }

        public string BillingUrl { get; set; } = "/billing";

        public IReadOnlyList<SubscriptionPaymentHistoryItemDto> Payments { get; set; }
            = Array.Empty<SubscriptionPaymentHistoryItemDto>();
    }

    public class StartSubtitleRecognitionResponse
    {
        public string TaskId { get; set; } = string.Empty;

        public int? RemainingQuota { get; set; }

        public int? RemainingTranscriptionMinutes { get; set; }

        public int? RemainingVideos { get; set; }
    }

    public class StartSubtitleRecognitionBatchRequest
    {
        public IReadOnlyList<string> YoutubeIds { get; set; } = Array.Empty<string>();

        public string? Language { get; set; }

        public string? CreatedBy { get; set; }
    }

    public class StartSubtitleRecognitionBatchResponse
    {
        public IReadOnlyList<string> TaskIds { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> InvalidItems { get; set; } = Array.Empty<string>();

        public int? RemainingQuota { get; set; }

        public int? RemainingTranscriptionMinutes { get; set; }

        public int? RemainingVideos { get; set; }
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

    public class AdminSubscriptionPlanDto
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal Price { get; set; }

        public string Currency { get; set; } = "RUB";

        public int IncludedTranscriptionMinutes { get; set; }

        public int IncludedVideos { get; set; }

        public bool IsActive { get; set; }

        public int Priority { get; set; }
    }

    public class SaveAdminSubscriptionPlanRequest
    {
        [Required]
        [MaxLength(64)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? Description { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        [Range(0, int.MaxValue)]
        public int IncludedTranscriptionMinutes { get; set; }

        [Range(0, int.MaxValue)]
        public int IncludedVideos { get; set; }

        public bool IsActive { get; set; } = true;

        public int Priority { get; set; }
    }
}
