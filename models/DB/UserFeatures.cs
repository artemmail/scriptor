using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum FeatureFlagSource
    {
        Plan = 0,
        Manual = 1,
        Promotion = 2,
        System = 3
    }

    [Table("UserFeatureFlags")]
    public class UserFeatureFlag
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string FeatureCode { get; set; } = string.Empty;

        [MaxLength(2048)]
        public string? Value { get; set; }

        public FeatureFlagSource Source { get; set; } = FeatureFlagSource.Plan;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ExpiresAt { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
