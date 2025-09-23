using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB

{
    public class ApplicationUser : IdentityUser
    {
        // Дополнительные поля (например, для платной подписки)
        public bool IsSubscribed { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }

        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;
    }
}
