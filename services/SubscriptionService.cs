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
using YandexSpeech.services.Models;

namespace YandexSpeech.services
{
    public class SubscriptionService : ISubscriptionService
    {
        private const string WelcomePlanCode = "welcome_free";

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
                .ThenBy(p => p.Name)
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
                .Where(s => s.IsLifetime || s.RemainingTranscriptionMinutes > 0 || s.RemainingVideos > 0)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<UserSubscription>> GetActiveSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var now = DateTime.UtcNow;
            return await _dbContext.UserSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
                .Where(s => s.EndDate == null || s.EndDate > now)
                .Where(s => s.IsLifetime || s.RemainingTranscriptionMinutes > 0 || s.RemainingVideos > 0)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(cancellationToken)
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

            var subscription = new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = planId,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                EndDate = ResolveEndDate(plan, now, isLifetime),
                GrantedTranscriptionMinutes = Math.Max(0, plan.IncludedTranscriptionMinutes),
                RemainingTranscriptionMinutes = Math.Max(0, plan.IncludedTranscriptionMinutes),
                GrantedVideos = Math.Max(0, plan.IncludedVideos),
                RemainingVideos = Math.Max(0, plan.IncludedVideos),
                AutoRenew = autoRenew,
                ExternalPaymentId = externalPaymentId,
                CreatedAt = now,
                IsLifetime = isLifetime
            };

            _dbContext.UserSubscriptions.Add(subscription);

            user.CurrentSubscriptionId = subscription.Id;
            if (subscription.IsLifetime)
            {
                user.HasLifetimeAccess = true;
            }

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
            subscription.EndDate ??= DateTime.UtcNow;

            if (subscription.User != null && subscription.User.CurrentSubscriptionId == subscriptionId)
            {
                subscription.User.CurrentSubscriptionId = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await RefreshUserCapabilitiesAsync(subscription.UserId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<SubscriptionQuotaBalance> GetQuotaBalanceAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            await EnsureWelcomePackageAsync(userId, cancellationToken).ConfigureAwait(false);

            var subscriptions = await GetActiveSubscriptionsAsync(userId, cancellationToken).ConfigureAwait(false);

            var totalMinutes = subscriptions.Sum(s => Math.Max(0, s.GrantedTranscriptionMinutes));
            var remainingMinutes = subscriptions.Any(s => s.IsLifetime)
                ? int.MaxValue
                : subscriptions.Sum(s => Math.Max(0, s.RemainingTranscriptionMinutes));

            var totalVideos = subscriptions.Sum(s => Math.Max(0, s.GrantedVideos));
            var remainingVideos = subscriptions.Any(s => s.IsLifetime)
                ? int.MaxValue
                : subscriptions.Sum(s => Math.Max(0, s.RemainingVideos));

            var packages = subscriptions
                .Select(s => new SubscriptionQuotaPackage
                {
                    SubscriptionId = s.Id,
                    PlanId = s.PlanId,
                    PlanCode = s.Plan?.Code ?? string.Empty,
                    PlanName = s.Plan?.Name ?? string.Empty,
                    RemainingTranscriptionMinutes = s.IsLifetime ? int.MaxValue : Math.Max(0, s.RemainingTranscriptionMinutes),
                    RemainingVideos = s.IsLifetime ? int.MaxValue : Math.Max(0, s.RemainingVideos),
                    CreatedAt = s.CreatedAt
                })
                .OrderBy(p => p.CreatedAt)
                .ToList();

            return new SubscriptionQuotaBalance
            {
                TotalTranscriptionMinutes = totalMinutes,
                RemainingTranscriptionMinutes = remainingMinutes,
                TotalVideos = totalVideos,
                RemainingVideos = remainingVideos,
                Packages = packages
            };
        }

        public async Task<bool> TryConsumeQuotaAsync(
            string userId,
            int transcriptionMinutes,
            int videos,
            string? reference = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var minutesToConsume = Math.Max(0, transcriptionMinutes);
            var videosToConsume = Math.Max(0, videos);

            if (minutesToConsume == 0 && videosToConsume == 0)
            {
                return true;
            }

            await EnsureWelcomePackageAsync(userId, cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            var subscriptions = await _dbContext.UserSubscriptions
                .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active)
                .Where(s => s.EndDate == null || s.EndDate > now)
                .Where(s => s.IsLifetime || s.RemainingTranscriptionMinutes > 0 || s.RemainingVideos > 0)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (subscriptions.Count == 0)
            {
                return false;
            }

            if (subscriptions.Any(s => s.IsLifetime))
            {
                return true;
            }

            var availableMinutes = subscriptions.Sum(s => Math.Max(0, s.RemainingTranscriptionMinutes));
            var availableVideos = subscriptions.Sum(s => Math.Max(0, s.RemainingVideos));

            if (availableMinutes < minutesToConsume || availableVideos < videosToConsume)
            {
                return false;
            }

            var minutesLeft = minutesToConsume;
            var videosLeft = videosToConsume;

            foreach (var subscription in subscriptions)
            {
                if (minutesLeft > 0)
                {
                    var takeMinutes = Math.Min(Math.Max(0, subscription.RemainingTranscriptionMinutes), minutesLeft);
                    if (takeMinutes > 0)
                    {
                        subscription.RemainingTranscriptionMinutes -= takeMinutes;
                        minutesLeft -= takeMinutes;
                    }
                }

                if (videosLeft > 0)
                {
                    var takeVideos = Math.Min(Math.Max(0, subscription.RemainingVideos), videosLeft);
                    if (takeVideos > 0)
                    {
                        subscription.RemainingVideos -= takeVideos;
                        videosLeft -= takeVideos;
                    }
                }

                if (minutesLeft == 0 && videosLeft == 0)
                {
                    break;
                }
            }

            foreach (var subscription in subscriptions.Where(s => !s.IsLifetime && s.RemainingTranscriptionMinutes <= 0 && s.RemainingVideos <= 0))
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.EndDate ??= now;
                subscription.CancelledAt ??= now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await RefreshUserCapabilitiesAsync(userId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Consumed quota for user {UserId}. Minutes={Minutes}, Videos={Videos}, Ref={Reference}",
                userId,
                minutesToConsume,
                videosToConsume,
                reference);

            return true;
        }

        public async Task EnsureWelcomePackageAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (user == null)
            {
                return;
            }

            var welcomePlan = await _dbContext.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.Price)
                .FirstOrDefaultAsync(p => p.Code == WelcomePlanCode || p.Price == 0m, cancellationToken)
                .ConfigureAwait(false);

            if (welcomePlan == null)
            {
                return;
            }

            var hasWelcome = await _dbContext.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && s.PlanId == welcomePlan.Id, cancellationToken)
                .ConfigureAwait(false);

            if (hasWelcome)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var subscription = new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanId = welcomePlan.Id,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                EndDate = null,
                GrantedTranscriptionMinutes = Math.Max(0, welcomePlan.IncludedTranscriptionMinutes),
                RemainingTranscriptionMinutes = Math.Max(0, welcomePlan.IncludedTranscriptionMinutes),
                GrantedVideos = Math.Max(0, welcomePlan.IncludedVideos),
                RemainingVideos = Math.Max(0, welcomePlan.IncludedVideos),
                AutoRenew = false,
                ExternalPaymentId = "welcome",
                CreatedAt = now,
                IsLifetime = false
            };

            _dbContext.UserSubscriptions.Add(subscription);

            if (!user.CurrentSubscriptionId.HasValue)
            {
                user.CurrentSubscriptionId = subscription.Id;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Granted welcome package {PlanCode} for user {UserId}", welcomePlan.Code, userId);
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
                "IncludedTranscriptionMinutes",
                "IncludedVideos"
            };

            UpsertFlag("CanHideCaptions", bool.TrueString);
            UpsertFlag("IncludedTranscriptionMinutes", plan.IncludedTranscriptionMinutes.ToString());
            UpsertFlag("IncludedVideos", plan.IncludedVideos.ToString());

            var toRemove = flags
                .Where(f => f.Source == FeatureFlagSource.Plan && !requiredCodes.Contains(f.FeatureCode))
                .ToList();

            if (toRemove.Count > 0)
            {
                _dbContext.UserFeatureFlags.RemoveRange(toRemove);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        private static DateTime? ResolveEndDate(SubscriptionPlan plan, DateTime from, bool isLifetime)
        {
            if (isLifetime)
            {
                return null;
            }

            if (plan.IncludedTranscriptionMinutes > 0 || plan.IncludedVideos > 0)
            {
                return null;
            }

            return plan.BillingPeriod switch
            {
                SubscriptionBillingPeriod.ThreeDays => from.AddDays(3),
                SubscriptionBillingPeriod.Monthly => from.AddMonths(1),
                SubscriptionBillingPeriod.Yearly => from.AddYears(1),
                SubscriptionBillingPeriod.OneTime => from.AddMonths(1),
                _ => from.AddMonths(1)
            };
        }
    }
}
