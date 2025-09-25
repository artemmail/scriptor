using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;
using YandexSpeech.services.Models;

namespace YandexSpeech.services.Interface
{
    public interface IUsageService
    {
        Task<UsageEvaluationResult> EvaluateDailyQuotaAsync(string userId, int requestedRecognitions, int? dailyLimit, bool hasUnlimitedQuota, CancellationToken cancellationToken = default);

        Task<RecognitionUsage> RegisterUsageAsync(string userId, int recognitionsCount, decimal chargeAmount, string currency, Guid? walletTransactionId = null, CancellationToken cancellationToken = default);
    }
}
