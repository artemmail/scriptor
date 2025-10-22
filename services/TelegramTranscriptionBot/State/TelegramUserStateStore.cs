using System.Collections.Concurrent;
using YandexSpeech.models.DTO.Telegram;

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

        public void UpdateCalendarStatus(long userId, TelegramCalendarStatusDto status, DateTime fetchedAt)
        {
            var state = _states.GetOrAdd(userId, static _ => new TelegramUserState());
            state.CalendarStatus = status;
            state.StatusFetchedAt = fetchedAt;
            state.HasCalendarConsent = status.HasCalendarAccess;
        }
    }
}
