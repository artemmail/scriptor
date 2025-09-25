using System;
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
    public class UsageService : IUsageService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<UsageService> _logger;

        public UsageService(MyDbContext dbContext, ILogger<UsageService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UsageEvaluationResult> EvaluateDailyQuotaAsync(string userId, int requestedRecognitions, int? dailyLimit, bool hasUnlimitedQuota, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");

            ResetCounterIfRequired(user);

            if (hasUnlimitedQuota || !dailyLimit.HasValue || dailyLimit.Value <= 0)
            {
                return new UsageEvaluationResult(true, int.MaxValue);
            }

            var remaining = dailyLimit.Value - user.RecognitionsToday;
            if (remaining < requestedRecognitions)
            {
                return new UsageEvaluationResult(false, Math.Max(remaining, 0), "Daily recognition limit exceeded");
            }

            return new UsageEvaluationResult(true, remaining - requestedRecognitions);
        }

        public async Task<RecognitionUsage> RegisterUsageAsync(string userId, int recognitionsCount, decimal chargeAmount, string currency, Guid? walletTransactionId = null, CancellationToken cancellationToken = default)
        {
            if (recognitionsCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recognitionsCount));
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"User '{userId}' was not found.");

            ResetCounterIfRequired(user);

            user.RecognitionsToday += recognitionsCount;

            var date = DateTime.UtcNow.Date;
            var usage = await _dbContext.RecognitionUsage
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Date == date, cancellationToken)
                .ConfigureAwait(false);

            if (usage == null)
            {
                usage = new RecognitionUsage
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Date = date,
                    RecognitionsCount = recognitionsCount,
                    ChargedAmount = chargeAmount,
                    Currency = currency,
                    WalletTransactionId = walletTransactionId
                };

                _dbContext.RecognitionUsage.Add(usage);
            }
            else
            {
                usage.RecognitionsCount += recognitionsCount;
                usage.ChargedAmount += chargeAmount;
                usage.WalletTransactionId ??= walletTransactionId;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Registered usage for user {UserId}: {Count} recognitions, charged {Amount} {Currency}", userId, recognitionsCount, chargeAmount, currency);

            return usage;
        }

        private void ResetCounterIfRequired(ApplicationUser user)
        {
            var now = DateTime.UtcNow;
            if (user.RecognitionsResetAt == null || user.RecognitionsResetAt <= now)
            {
                user.RecognitionsToday = 0;
                user.RecognitionsResetAt = now.Date.AddDays(1);
            }
        }
    }
}
