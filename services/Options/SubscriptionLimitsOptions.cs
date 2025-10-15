using System;

namespace YandexSpeech.services.Options
{
    public class SubscriptionLimitsOptions
    {
        public int FreeYoutubeRecognitionsPerDay { get; set; } = 3;

        public int FreeTranscriptionsPerMonth { get; set; } = 2;

        public string BillingRelativeUrl { get; set; } = "/billing";

        public int ExpirationCheckIntervalMinutes { get; set; } = 30;

        public string GetBillingUrlOrDefault()
        {
            return string.IsNullOrWhiteSpace(BillingRelativeUrl) ? "/billing" : BillingRelativeUrl;
        }

        public TimeSpan GetExpirationInterval()
        {
            var minutes = Math.Max(5, ExpirationCheckIntervalMinutes);
            return TimeSpan.FromMinutes(minutes);
        }
    }
}
