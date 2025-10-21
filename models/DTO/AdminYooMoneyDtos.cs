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
}
