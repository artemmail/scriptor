using System;
using System.Collections.Generic;

namespace YandexSpeech.models.DTO
{
    public class AdminYooMoneyOperationDto
    {
        public string? OperationId { get; set; }

        public string? Title { get; set; }

        public decimal? Amount { get; set; }

        public DateTime? DateTime { get; set; }

        public string? Status { get; set; }

        public IDictionary<string, object?>? AdditionalData { get; set; }
    }

    public class AdminYooMoneyOperationDetailsDto : AdminYooMoneyOperationDto
    {
    }

    public class AdminPaymentOperationDetailsDto
    {
        public Guid Id { get; set; }

        public string? UserId { get; set; }

        public string? UserEmail { get; set; }

        public string? UserDisplayName { get; set; }

        public string? Provider { get; set; }

        public string? Status { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "RUB";

        public DateTime RequestedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string? Payload { get; set; }

        public string? ExternalOperationId { get; set; }

        public Guid? WalletTransactionId { get; set; }
    }
}
