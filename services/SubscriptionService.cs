using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(MyDbContext dbContext, ILogger<SubscriptionService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.SubscriptionPlans.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(p => p.IsActive);
            }

            return await query
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.Price)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<SubscriptionPlan> SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            if (plan.Id == Guid.Empty)
            {
                plan.Id = Guid.NewGuid();
                plan.CreatedAt = DateTime.UtcNow;
                _dbContext.SubscriptionPlans.Add(plan);
            }
            else
            {
                plan.UpdatedAt = DateTime.UtcNow;
                _dbContext.SubscriptionPlans.Update(plan);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return plan;
        }

        public async Task<UserSubscription?> GetActiveSubscriptionAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var now = DateTime.UtcNow;
            return await _dbContext.UserSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
                .Where(s => s.EndDate == null || s.EndDate > now)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<UserSubscription> ActivateSubscriptionAsync(string userId, Guid planId, bool autoRenew = false, bool isLifetimeOverride = false, string? externalPaymentId = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var plan = await _dbContext.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
                .ConfigureAwait(false);

            if (plan == null)
            {
                throw new InvalidOperationException($"Subscription plan '{planId}' was not found.");
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");

            var now = DateTime.UtcNow;

            var isLifetime = isLifetimeOverride || plan.BillingPeriod == SubscriptionBillingPeriod.Lifetime;
            var endDate = isLifetime ? (DateTime?)null : CalculateEndDate(plan, now);

            var subscription = new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = planId,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                EndDate = endDate,
                AutoRenew = autoRenew,
                ExternalPaymentId = externalPaymentId,
                CreatedAt = now,
                IsLifetime = isLifetime
            };

            var existing = await _dbContext.UserSubscriptions
                .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var active in existing)
            {
                active.Status = SubscriptionStatus.Expired;
                active.EndDate ??= now;
                active.CancelledAt = now;
            }

            _dbContext.UserSubscriptions.Add(subscription);

            user.CurrentSubscriptionId = subscription.Id;
            if (subscription.IsLifetime)
            {
                user.HasLifetimeAccess = true;
            }

            user.RecognitionsToday = 0;
            user.RecognitionsResetAt = now.Date.AddDays(1);

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await RefreshUserCapabilitiesAsync(userId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Activated subscription {SubscriptionId} for user {UserId}", subscription.Id, userId);

            return subscription;
        }

        public async Task CancelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        {
            if (subscriptionId == Guid.Empty)
            {
                return;
            }

            var subscription = await _dbContext.UserSubscriptions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
                .ConfigureAwait(false);

            if (subscription == null)
            {
                return;
            }

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAt = DateTime.UtcNow;

            if (subscription.User != null && subscription.User.CurrentSubscriptionId == subscriptionId)
            {
                subscription.User.CurrentSubscriptionId = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await RefreshUserCapabilitiesAsync(subscription.UserId, cancellationToken).ConfigureAwait(false);
        }

        public async Task RefreshUserCapabilitiesAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var user = await _dbContext.Users
                .Include(u => u.FeatureFlags)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (user == null)
            {
                return;
            }

            var activeSubscription = await GetActiveSubscriptionAsync(userId, cancellationToken).ConfigureAwait(false);
            if (activeSubscription == null)
            {
                user.CurrentSubscriptionId = null;

                var planFlags = user.FeatureFlags
                    .Where(f => f.Source == FeatureFlagSource.Plan)
                    .ToList();

                if (planFlags.Count > 0)
                {
                    _dbContext.UserFeatureFlags.RemoveRange(planFlags);
                }

                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            user.CurrentSubscriptionId = activeSubscription.Id;

            if (activeSubscription.IsLifetime)
            {
                user.HasLifetimeAccess = true;
            }

            await SyncFeatureFlagsAsync(user, activeSubscription.Plan, cancellationToken).ConfigureAwait(false);
        }

        private async Task SyncFeatureFlagsAsync(ApplicationUser user, SubscriptionPlan? plan, CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                return;
            }

            var flags = await _dbContext.UserFeatureFlags
                .Where(f => f.UserId == user.Id && f.Source == FeatureFlagSource.Plan)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            void UpsertFlag(string code, string value)
            {
                var existing = flags.FirstOrDefault(f => f.FeatureCode == code);
                if (existing == null)
                {
                    _dbContext.UserFeatureFlags.Add(new UserFeatureFlag
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        FeatureCode = code,
                        Value = value,
                        Source = FeatureFlagSource.Plan,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Value = value;
                    existing.CreatedAt = DateTime.UtcNow;
                }
            }

            var requiredCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CanHideCaptions",
                "IsUnlimitedRecognitions"
            };

            UpsertFlag("CanHideCaptions", plan.CanHideCaptions.ToString());
            UpsertFlag("IsUnlimitedRecognitions", plan.IsUnlimitedRecognitions.ToString());

            if (plan.MaxRecognitionsPerDay.HasValue)
            {
                UpsertFlag("MaxRecognitionsPerDay", plan.MaxRecognitionsPerDay.Value.ToString());
                requiredCodes.Add("MaxRecognitionsPerDay");
            }

            var toRemove = flags
                .Where(f => f.Source == FeatureFlagSource.Plan && !requiredCodes.Contains(f.FeatureCode))
                .ToList();

            if (toRemove.Count > 0)
            {
                _dbContext.UserFeatureFlags.RemoveRange(toRemove);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        private static DateTime CalculateEndDate(SubscriptionPlan plan, DateTime from)
        {
            return plan.BillingPeriod switch
            {
                SubscriptionBillingPeriod.Monthly => from.AddMonths(1),
                SubscriptionBillingPeriod.Yearly => from.AddYears(1),
                SubscriptionBillingPeriod.OneTime => from.AddMonths(1),
                _ => from
            };
        }
    }
}
