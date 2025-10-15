using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum SubscriptionBillingPeriod
    {
        OneTime = 0,
        Monthly = 1,
        Yearly = 2,
        Lifetime = 3,
        ThreeDays = 4
    }

    public enum SubscriptionStatus
    {
        Pending = 0,
        Active = 1,
        PastDue = 2,
        Cancelled = 3,
        Expired = 4
    }

    public enum SubscriptionInvoiceStatus
    {
        Draft = 0,
        Issued = 1,
        Paid = 2,
        Cancelled = 3,
        Failed = 4
    }

    [Table("SubscriptionPlans")]
    public class SubscriptionPlan
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? Description { get; set; }

        public SubscriptionBillingPeriod BillingPeriod { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public int? MaxRecognitionsPerDay { get; set; }

        public bool CanHideCaptions { get; set; } = true;

        public bool IsUnlimitedRecognitions { get; set; }

        public int Priority { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
    }

    [Table("UserSubscriptions")]
    public class UserSubscription
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public Guid PlanId { get; set; }

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;

        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        public DateTime? EndDate { get; set; }

        public bool AutoRenew { get; set; }

        [MaxLength(128)]
        public string? ExternalPaymentId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CancelledAt { get; set; }

        public bool IsLifetime { get; set; }

        public ApplicationUser? User { get; set; }

        public SubscriptionPlan? Plan { get; set; }

        public ICollection<SubscriptionInvoice> Invoices { get; set; } = new List<SubscriptionInvoice>();
    }

    [Table("SubscriptionInvoices")]
    public class SubscriptionInvoice
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid UserSubscriptionId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public SubscriptionInvoiceStatus Status { get; set; } = SubscriptionInvoiceStatus.Draft;

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        [MaxLength(64)]
        public string? PaymentProvider { get; set; }

        [MaxLength(128)]
        public string? ExternalInvoiceId { get; set; }

        public string? Payload { get; set; }

        public UserSubscription? UserSubscription { get; set; }
    }
}
