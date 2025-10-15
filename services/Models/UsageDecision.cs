namespace YandexSpeech.services.Models
{
    public sealed class UsageDecision
    {
        private UsageDecision(bool isAllowed, int? remainingQuota, string? message, string? paymentUrl)
        {
            IsAllowed = isAllowed;
            RemainingQuota = remainingQuota;
            Message = message;
            PaymentUrl = paymentUrl;
        }

        public bool IsAllowed { get; }

        public int? RemainingQuota { get; }

        public string? Message { get; }

        public string? PaymentUrl { get; }

        public static UsageDecision Allowed(int? remainingQuota = null)
        {
            return new UsageDecision(true, remainingQuota, null, null);
        }

        public static UsageDecision Denied(string message, string paymentUrl, int? remainingQuota = null)
        {
            return new UsageDecision(false, remainingQuota, message, paymentUrl);
        }
    }
}
