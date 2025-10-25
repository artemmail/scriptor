using System;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB
{
    public class UserGoogleToken
    {
        [Key]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string TokenType { get; set; } = GoogleTokenTypes.Calendar;

        [MaxLength(1024)]
        public string? Scope { get; set; }

        [MaxLength(4096)]
        public string? AccessToken { get; set; }

        public DateTime? AccessTokenExpiresAt { get; set; }

        public DateTime? AccessTokenUpdatedAt { get; set; }

        [MaxLength(4096)]
        public string? RefreshToken { get; set; }

        public DateTime? RefreshTokenExpiresAt { get; set; }

        public DateTime? ConsentGrantedAt { get; set; }

        public DateTime? ConsentDeclinedAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public virtual ApplicationUser User { get; set; } = null!;
    }

    public static class GoogleTokenTypes
    {
        public const string Calendar = "calendar";
    }
}
