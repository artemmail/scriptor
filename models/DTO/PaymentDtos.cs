using System;
using System.ComponentModel.DataAnnotations;
using YandexSpeech.models.DB;

namespace YandexSpeech.models.DTO
{
    public class SubscriptionPlanDto
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public SubscriptionBillingPeriod BillingPeriod { get; set; }

        public decimal Price { get; set; }

        public string Currency { get; set; } = "RUB";

        public int? MaxRecognitionsPerDay { get; set; }

        public bool CanHideCaptions { get; set; }

        public bool IsUnlimitedRecognitions { get; set; }

        public bool IsLifetime { get; set; }
    }

    public class PaymentInitResponse
    {
        public Guid OperationId { get; set; }

        public string PaymentUrl { get; set; } = string.Empty;
    }

    public class CreateSubscriptionPaymentRequest
    {
        [Required]
        [MaxLength(64)]
        public string PlanCode { get; set; } = string.Empty;
    }

    public class CreateWalletDepositRequest
    {
        [Range(1, 1_000_000)]
        public decimal Amount { get; set; }

        [MaxLength(256)]
        public string? Comment { get; set; }
    }

    public class WalletBalanceDto
    {
        public decimal Balance { get; set; }

        public string Currency { get; set; } = "RUB";
    }
}
