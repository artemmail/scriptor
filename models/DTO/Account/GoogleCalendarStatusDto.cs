using System;

namespace YandexSpeech.models.DTO.Account
{
    public sealed class GoogleCalendarStatusDto
    {
        public bool IsGoogleAccount { get; set; }

        public bool IsConnected { get; set; }

        public bool HasRefreshToken { get; set; }

        public bool ConsentGranted => ConsentGrantedAt.HasValue && !TokensRevokedAt.HasValue;

        public DateTime? ConsentGrantedAt { get; set; }

        public DateTime? ConsentDeclinedAt { get; set; }

        public DateTime? TokensRevokedAt { get; set; }

        public DateTime? AccessTokenUpdatedAt { get; set; }

        public DateTime? AccessTokenExpiresAt { get; set; }

        public DateTime? RefreshTokenExpiresAt { get; set; }

        public bool CanManageFromBot => IsGoogleAccount && IsConnected;
    }
}
