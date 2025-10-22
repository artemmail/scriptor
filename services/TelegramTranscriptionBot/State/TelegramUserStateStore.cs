using System.Collections.Concurrent;

namespace YandexSpeech.services.TelegramTranscriptionBot.State
{
    public sealed class TelegramUserStateStore
    {
        private readonly ConcurrentDictionary<long, TelegramUserState> _states = new();

        public TelegramUserState GetOrCreate(long userId)
        {
            return _states.GetOrAdd(userId, static _ => new TelegramUserState());
        }

        public bool TryGet(long userId, out TelegramUserState? state)
        {
            var found = _states.TryGetValue(userId, out var existing);
            state = existing;
            return found;
        }

        public void SetCalendarConsent(long userId, bool hasConsent)
        {
            var state = _states.GetOrAdd(userId, static _ => new TelegramUserState());
            state.HasCalendarConsent = hasConsent;
        }
    }
}
