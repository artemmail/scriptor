using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum PaymentProvider
    {
        Unknown = 0,
        YooMoney = 1,
        BankCard = 2,
        Manual = 3
    }

    public enum PaymentOperationStatus
    {
        Pending = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3
    }

    [Table("PaymentOperations")]
    public class PaymentOperation
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public PaymentProvider Provider { get; set; } = PaymentProvider.Unknown;

        [MaxLength(128)]
        public string? ExternalOperationId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public PaymentOperationStatus Status { get; set; } = PaymentOperationStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }

        public string? Payload { get; set; }

        public Guid? WalletTransactionId { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
