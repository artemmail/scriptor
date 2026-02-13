using System.Collections.Generic;

namespace YandexSpeech.services.Models
{
    public sealed class UsageDecision
    {
        private UsageDecision(
            bool isAllowed,
            int? remainingQuota,
            int? remainingTranscriptionMinutes,
            int? remainingVideos,
            int? requestedTranscriptionMinutes,
            int? maxUploadMinutes,
            string? message,
            string? paymentUrl,
            IReadOnlyList<string>? recognizedTitles)
        {
            IsAllowed = isAllowed;
            RemainingQuota = remainingQuota;
            RemainingTranscriptionMinutes = remainingTranscriptionMinutes;
            RemainingVideos = remainingVideos;
            RequestedTranscriptionMinutes = requestedTranscriptionMinutes;
            MaxUploadMinutes = maxUploadMinutes;
            Message = message;
            PaymentUrl = paymentUrl;
            RecognizedTitles = recognizedTitles;
        }

        public bool IsAllowed { get; }

        public int? RemainingQuota { get; }

        public int? RemainingTranscriptionMinutes { get; }

        public int? RemainingVideos { get; }

        public int? RequestedTranscriptionMinutes { get; }

        public int? MaxUploadMinutes { get; }

        public string? Message { get; }

        public string? PaymentUrl { get; }

        public IReadOnlyList<string>? RecognizedTitles { get; }

        public static UsageDecision Allowed(
            int? remainingQuota = null,
            int? remainingTranscriptionMinutes = null,
            int? remainingVideos = null)
        {
            return new UsageDecision(
                true,
                remainingQuota,
                remainingTranscriptionMinutes,
                remainingVideos,
                null,
                null,
                null,
                null,
                null);
        }

        public static UsageDecision Denied(
            string message,
            string paymentUrl,
            int? remainingQuota = null,
            int? remainingTranscriptionMinutes = null,
            int? remainingVideos = null,
            int? requestedTranscriptionMinutes = null,
            int? maxUploadMinutes = null,
            IReadOnlyList<string>? recognizedTitles = null)
        {
            return new UsageDecision(
                false,
                remainingQuota,
                remainingTranscriptionMinutes,
                remainingVideos,
                requestedTranscriptionMinutes,
                maxUploadMinutes,
                message,
                paymentUrl,
                recognizedTitles);
        }
    }
}
