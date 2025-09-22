using Microsoft.AspNetCore.Identity;
using System;

namespace YandexSpeech.models.DB

{
    public class ApplicationUser : IdentityUser
    {
        // Дополнительные поля (например, для платной подписки)
        public bool IsSubscribed { get; set; }
        public DateTime? SubscriptionExpiry { get; set; }
    }
}