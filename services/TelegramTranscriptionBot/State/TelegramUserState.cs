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
        private long _statusFetchedAtBinary = DateTime.MinValue.ToBinary();
        private TelegramCalendarEventCandidate? _pendingCalendarEvent;

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
            get => DateTime.FromBinary(Volatile.Read(ref _statusFetchedAtBinary));
            set => Volatile.Write(ref _statusFetchedAtBinary, value.ToBinary());
        }

        public TelegramCalendarEventCandidate? PendingCalendarEvent
        {
            get => Volatile.Read(ref _pendingCalendarEvent);
            set => Volatile.Write(ref _pendingCalendarEvent, value);
        }
    }

    public sealed class TelegramCalendarEventCandidate
    {
        public string Title { get; init; } = string.Empty;

        public string? Description { get; init; }
        public string SourceText { get; init; } = string.Empty;

        public DateTimeOffset StartsAt { get; init; }

        public DateTimeOffset? EndsAt { get; init; }

        public string? TimeZone { get; init; }

        public int SourceMessageId { get; init; }
    }
}
