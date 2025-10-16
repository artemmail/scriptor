using System;
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

            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!userExists)
            {
                throw new InvalidOperationException($"User '{userId}' was not found.");
            }

            if (hasUnlimitedQuota || !dailyLimit.HasValue || dailyLimit.Value <= 0)
            {
                return new UsageEvaluationResult(true, int.MaxValue);
            }

            var usedRecognitions = await GetRecognitionsLast24HoursAsync(userId, cancellationToken).ConfigureAwait(false);
            var remaining = dailyLimit.Value - usedRecognitions;
            if (remaining < requestedRecognitions)
            {
                return new UsageEvaluationResult(false, Math.Max(remaining, 0), "Recognition limit for the last 24 hours exceeded");
            }

            return new UsageEvaluationResult(true, remaining - requestedRecognitions);
        }

        public async Task<RecognitionUsage> RegisterUsageAsync(string userId, int recognitionsCount, decimal chargeAmount, string currency, Guid? walletTransactionId = null, CancellationToken cancellationToken = default)
        {
            if (recognitionsCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recognitionsCount));
            }

            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!userExists)
            {
                throw new InvalidOperationException($"User '{userId}' was not found.");
            }

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

        private Task<int> GetRecognitionsLast24HoursAsync(string userId, CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddDays(-1);

            return _dbContext.YoutubeCaptionTasks
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .Where(t => t.Done)
                .Where(t => !t.Status.HasValue || (int)t.Status.Value != 990)
                .Where(t =>
                    (t.ModifiedAt.HasValue && t.ModifiedAt.Value >= cutoff) ||
                    (!t.ModifiedAt.HasValue && t.CreatedAt.HasValue && t.CreatedAt.Value >= cutoff))
                .CountAsync(cancellationToken);
        }
    }
}
