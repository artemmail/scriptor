using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Interface
{
    public interface ISubscriptionService
    {
        Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

        Task<SubscriptionPlan> SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default);

        Task<UserSubscription?> GetActiveSubscriptionAsync(string userId, CancellationToken cancellationToken = default);

        Task<UserSubscription> ActivateSubscriptionAsync(string userId, Guid planId, bool autoRenew = false, bool isLifetimeOverride = false, string? externalPaymentId = null, CancellationToken cancellationToken = default);

        Task CancelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

        Task RefreshUserCapabilitiesAsync(string userId, CancellationToken cancellationToken = default);
    }
}
