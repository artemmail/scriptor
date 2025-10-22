using System;
using System.Threading;
using YandexSpeech.models.DTO.Telegram;

namespace YandexSpeech.services.TelegramTranscriptionBot.State
{
    public sealed class TelegramUserState
    {
        private volatile bool _hasCalendarConsent;
        private volatile bool _calendarScenarioRequested;
        private TelegramCalendarStatusDto _calendarStatus = TelegramCalendarStatusDto.NotLinked();
        private DateTime _statusFetchedAt = DateTime.MinValue;

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

        public TelegramCalendarStatusDto CalendarStatus
        {
            get => Volatile.Read(ref _calendarStatus);
            set => Volatile.Write(ref _calendarStatus, value);
        }

        public DateTime StatusFetchedAt
        {
            get => Volatile.Read(ref _statusFetchedAt);
            set => Volatile.Write(ref _statusFetchedAt, value);
        }
    }
}
