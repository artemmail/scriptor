using System.Threading;

namespace YandexSpeech.services
{
    public sealed class ServerRenderModeService
    {
        private long _enabledUntilUnixSeconds;

        public DateTimeOffset EnableFor(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                duration = TimeSpan.FromSeconds(1);
            }

            var enabledUntil = DateTimeOffset.UtcNow.Add(duration);
            Interlocked.Exchange(ref _enabledUntilUnixSeconds, enabledUntil.ToUnixTimeSeconds());
            return enabledUntil;
        }

        public bool IsEnabled()
        {
            var enabledUntil = Interlocked.Read(ref _enabledUntilUnixSeconds);
            return enabledUntil > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public DateTimeOffset? GetEnabledUntilUtc()
        {
            var enabledUntil = Interlocked.Read(ref _enabledUntilUnixSeconds);
            return enabledUntil > 0
                ? DateTimeOffset.FromUnixTimeSeconds(enabledUntil)
                : null;
        }
    }
}
