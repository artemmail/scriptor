using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB

{
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Дата и время регистрации пользователя (UTC).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Текущая активная подписка пользователя (если есть).
        /// </summary>
        public Guid? CurrentSubscriptionId { get; set; }

        public virtual UserSubscription? CurrentSubscription { get; set; }

        /// <summary>
        /// Флаг пожизненного доступа к премиум-функциям.
        /// </summary>
        public bool HasLifetimeAccess { get; set; }

        /// <summary>
        /// Навигационное свойство для активных возможностей (feature flags).
        /// </summary>
        public ICollection<UserFeatureFlag> FeatureFlags { get; set; } = new List<UserFeatureFlag>();

        public DateTime? GoogleCalendarConsentAt { get; set; }

        [MaxLength(4096)]
        public string? GoogleAccessToken { get; set; }

        [MaxLength(4096)]
        public string? GoogleRefreshToken { get; set; }

        public DateTime? GoogleAccessTokenExpiresAt { get; set; }

        /// <summary>
        /// Навигационное свойство для кошелька.
        /// </summary>
        public virtual UserWallet? Wallet { get; set; }

        public ICollection<UserSubscription> Subscriptions { get; set; } = new List<UserSubscription>();

        public ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    }
}
