using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DTO
{
    public sealed class MobileAuthResultDto
    {
        public string AccessToken { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;

        public UserProfileDto User { get; set; } = new();
    }

    public sealed class MobileRefreshTokenRequest
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public sealed class MobileLogoutRequest
    {
        public string? RefreshToken { get; set; }
    }
}
