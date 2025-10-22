using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB
{
    public class TelegramAccountLink
    {
        public Guid Id { get; set; }

        public long TelegramId { get; set; }

        [MaxLength(255)]
        public string? Username { get; set; }

        [MaxLength(255)]
        public string? FirstName { get; set; }

        [MaxLength(255)]
        public string? LastName { get; set; }

        [MaxLength(10)]
        public string? LanguageCode { get; set; }

        [MaxLength(450)]
        public string? UserId { get; set; }

        public TelegramAccountLinkStatus Status { get; set; } = TelegramAccountLinkStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LinkedAt { get; set; }

        public DateTime? LastActivityAt { get; set; }

        public DateTime? LastStatusCheckAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        public virtual ApplicationUser? User { get; set; }

        public ICollection<TelegramLinkToken> Tokens { get; set; } = new List<TelegramLinkToken>();
    }

    public enum TelegramAccountLinkStatus
    {
        Pending = 0,
        Linked = 1,
        Revoked = 2
    }

    public class TelegramLinkToken
    {
        public Guid Id { get; set; }

        public Guid LinkId { get; set; }

        public virtual TelegramAccountLink Link { get; set; } = null!;

        [MaxLength(512)]
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public DateTime? ConsumedAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        [MaxLength(64)]
        public string Purpose { get; set; } = TelegramLinkTokenPurposes.Link;

        public bool IsOneTime { get; set; } = true;
    }

    public static class TelegramLinkTokenPurposes
    {
        public const string Link = "link";
    }
}
