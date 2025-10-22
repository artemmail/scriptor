using System;

namespace YandexSpeech.models.DTO.Profile
{
    public class ProfileIndexViewModel
    {
        public string Email { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public GoogleCalendarStatusViewModel GoogleCalendar { get; set; } = new();
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
}
