using System.Threading;

namespace YandexSpeech.services.TelegramTranscriptionBot.State
{
    public sealed class TelegramUserState
    {
        private volatile bool _hasCalendarConsent;
        private volatile bool _calendarScenarioRequested;

        public bool HasCalendarConsent
        {
            get => Volatile.Read(ref _hasCalendarConsent);
            set => Volatile.Write(ref _hasCalendarConsent, value);
        }

        public bool CalendarScenarioRequested
        {
            get => Volatile.Read(ref _calendarScenarioRequested);
            set => Volatile.Write(ref _calendarScenarioRequested, value);
        }
    }
}
