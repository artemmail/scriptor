using System;
using YandexSpeech.models.DTO.Telegram;

namespace YandexSpeech.models.DTO.Profile
{
    public class ProfileIndexViewModel
    {
        public string Email { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public GoogleCalendarStatusViewModel GoogleCalendar { get; set; } = new();

        public TelegramStatusViewModel Telegram { get; set; } = new();
    }

    public class GoogleCalendarStatusViewModel
    {
        public bool IsConnected { get; set; }

        public bool HasRefreshToken { get; set; }

        public DateTime? ConsentAt { get; set; }

        public DateTime? AccessTokenUpdatedAt { get; set; }

        public DateTime? AccessTokenExpiresAt { get; set; }

        public DateTime? RefreshTokenExpiresAt { get; set; }

        public DateTime? TokensRevokedAt { get; set; }
    }

    public class TelegramStatusViewModel
    {
        public bool IsLinked { get; set; }

        public bool HasCalendarAccess { get; set; }

        public bool GoogleAuthorized { get; set; }

        public bool AccessTokenExpired { get; set; }

        public bool HasRequiredScope { get; set; }

        public string State { get; set; } = TelegramIntegrationStates.NotLinked;

        public string? DetailCode { get; set; }

        public string? PermissionScope { get; set; }

        public DateTime? LinkedAt { get; set; }

        public DateTime? LastActivityAt { get; set; }

        public DateTime? LastStatusCheckAt { get; set; }
    }
}
