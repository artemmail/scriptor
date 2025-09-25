namespace YandexSpeech.services.Models
{
    public sealed class UsageEvaluationResult
    {
        public UsageEvaluationResult(bool isAllowed, int remainingQuota, string? reason = null)
        {
            IsAllowed = isAllowed;
            RemainingQuota = remainingQuota;
            Reason = reason;
        }

        public bool IsAllowed { get; }

        public int RemainingQuota { get; }

        public string? Reason { get; }
    }
}
