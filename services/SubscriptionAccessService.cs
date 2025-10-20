using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DB;
using YandexSpeech.services.Models;
using YandexSpeech.services.Options;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public class SubscriptionAccessService : ISubscriptionAccessService
    {
        private readonly MyDbContext _dbContext;
        private readonly ISubscriptionService _subscriptionService;
        private readonly SubscriptionLimitsOptions _options;
        private readonly ILogger<SubscriptionAccessService> _logger;

        public SubscriptionAccessService(
            MyDbContext dbContext,
            ISubscriptionService subscriptionService,
            IOptions<SubscriptionLimitsOptions> options,
            ILogger<SubscriptionAccessService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UsageDecision> AuthorizeYoutubeRecognitionAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");

            var subscription = await _subscriptionService
                .GetActiveSubscriptionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var billingUrl = _options.GetBillingUrlOrDefault();
            var plan = subscription?.Plan;

            var hasUnlimited = user.HasLifetimeAccess
                || (subscription?.IsLifetime ?? false)
                || (plan?.IsUnlimitedRecognitions ?? false);

            if (hasUnlimited)
            {
                return UsageDecision.Allowed(null);
            }

            var dailyLimit = plan?.MaxRecognitionsPerDay ?? _options.FreeYoutubeRecognitionsPerDay;
            if (dailyLimit <= 0)
            {
                return UsageDecision.Allowed(null);
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            var usageQuery = _dbContext.YoutubeCaptionTasks
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .Where(t => t.Status != RecognizeStatus.Error)
                .Where(t =>
                    (t.ModifiedAt.HasValue && t.ModifiedAt.Value >= cutoff) ||
                    (t.CreatedAt.HasValue && t.CreatedAt.Value >= cutoff));

            var usedRecognitions = await usageQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            if (usedRecognitions >= dailyLimit)
            {
                var message = subscription != null
                    ? "Достигнут дневной лимит распознаваний для текущей подписки. Попробуйте завтра или продлите подписку."
                    : $"Бесплатный тариф включает {_options.FreeYoutubeRecognitionsPerDay} распознавания в день. Оформите подписку, чтобы снять ограничение.";

                var remainingQuota = Math.Max(dailyLimit - usedRecognitions, 0);
                _logger.LogInformation("User {UserId} exceeded daily recognition limit. Remaining {Remaining}", userId, remainingQuota);
                var recognizedTitles = await usageQuery
                    .Where(t => t.Done)
                    .OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt ?? DateTime.MinValue)
                    .Select(t => string.IsNullOrWhiteSpace(t.Title) ? t.Id : t.Title!)
                    .Take(3)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (recognizedTitles.Count > 0)
                {
                    message = string.Concat(
                        message,
                        " Уже распознаны: ",
                        string.Join(", ", recognizedTitles),
                        ".");
                }

                return UsageDecision.Denied(
                    message,
                    billingUrl,
                    remainingQuota,
                    recognizedTitles);
            }

            var remaining = Math.Max(dailyLimit - usedRecognitions - 1, 0);

            return UsageDecision.Allowed(remaining);
        }

        public async Task<UsageDecision> AuthorizeTranscriptionAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");

            var subscription = await _subscriptionService
                .GetActiveSubscriptionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var billingUrl = _options.GetBillingUrlOrDefault();

            if (user.HasLifetimeAccess || subscription != null)
            {
                return UsageDecision.Allowed(null);
            }

            var limit = Math.Max(0, _options.FreeTranscriptionsPerMonth);
            if (limit == 0)
            {
                return UsageDecision.Allowed(null);
            }

            var now = DateTime.UtcNow;
            var periodCode = $"usage:transcriptions:{now:yyyy-MM}";

            var usageFlag = await _dbContext.UserFeatureFlags
                .FirstOrDefaultAsync(f => f.UserId == userId && f.FeatureCode == periodCode, cancellationToken)
                .ConfigureAwait(false);

            var used = 0;
            if (usageFlag != null && !string.IsNullOrWhiteSpace(usageFlag.Value) && int.TryParse(usageFlag.Value, out var parsed))
            {
                used = Math.Max(0, parsed);
            }

            if (used >= limit)
            {
                var message = $"Бесплатный тариф включает {limit} транскрибации в месяц. Оформите подписку, чтобы продолжить без ограничений.";
                _logger.LogInformation("User {UserId} reached monthly transcription limit", userId);
                return UsageDecision.Denied(message, billingUrl, 0);
            }

            var updatedValue = used + 1;
            var nextReset = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

            if (usageFlag == null)
            {
                usageFlag = new UserFeatureFlag
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FeatureCode = periodCode,
                    Source = FeatureFlagSource.System,
                    Value = updatedValue.ToString(),
                    CreatedAt = now,
                    ExpiresAt = nextReset
                };

                _dbContext.UserFeatureFlags.Add(usageFlag);
            }
            else
            {
                usageFlag.Value = updatedValue.ToString();
                usageFlag.CreatedAt = now;
                usageFlag.ExpiresAt = nextReset;
                usageFlag.Source = FeatureFlagSource.System;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return UsageDecision.Allowed(Math.Max(limit - updatedValue, 0));
        }
    }
}
