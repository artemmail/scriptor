using System;
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

        public async Task<UsageDecision> AuthorizeYoutubeRecognitionAsync(
            string userId,
            int requestedVideos = 1,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var billingUrl = _options.GetBillingUrlOrDefault();

            var userExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!userExists)
            {
                throw new InvalidOperationException($"User '{userId}' was not found.");
            }

            var normalizedRequestedVideos = Math.Max(1, requestedVideos);
            var balance = await _subscriptionService
                .GetQuotaBalanceAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var remainingVideos = balance.RemainingVideos;
            var remainingMinutes = balance.RemainingTranscriptionMinutes;

            if (remainingVideos == int.MaxValue)
            {
                return UsageDecision.Allowed(
                    remainingQuota: null,
                    remainingTranscriptionMinutes: remainingMinutes == int.MaxValue ? null : remainingMinutes,
                    remainingVideos: null);
            }

            if (remainingVideos >= normalizedRequestedVideos)
            {
                var afterVideos = Math.Max(remainingVideos - normalizedRequestedVideos, 0);
                return UsageDecision.Allowed(
                    remainingQuota: afterVideos,
                    remainingTranscriptionMinutes: remainingMinutes,
                    remainingVideos: afterVideos);
            }

            var message = remainingVideos > 0
                ? $"Недостаточно видео-кредитов: доступно {remainingVideos}, требуется {normalizedRequestedVideos}. Пополните пакет в биллинге."
                : "Лимит видео исчерпан. Пополните пакет в биллинге.";

            _logger.LogInformation(
                "User {UserId} exceeded video quota. RemainingVideos={RemainingVideos}, RequestedVideos={RequestedVideos}",
                userId,
                remainingVideos,
                normalizedRequestedVideos);

            return UsageDecision.Denied(
                message,
                billingUrl,
                remainingQuota: remainingVideos,
                remainingTranscriptionMinutes: remainingMinutes,
                remainingVideos: remainingVideos);
        }

        public async Task<UsageDecision> AuthorizeTranscriptionAsync(
            string userId,
            int requestedTranscriptionMinutes,
            CancellationToken cancellationToken = default)
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

            var billingUrl = _options.GetBillingUrlOrDefault();
            var requestedMinutes = Math.Max(1, requestedTranscriptionMinutes);

            var balance = await _subscriptionService
                .GetQuotaBalanceAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var remainingMinutes = balance.RemainingTranscriptionMinutes;
            var remainingVideos = balance.RemainingVideos;

            if (remainingMinutes == int.MaxValue)
            {
                return UsageDecision.Allowed(
                    remainingQuota: remainingVideos == int.MaxValue ? null : remainingVideos,
                    remainingTranscriptionMinutes: null,
                    remainingVideos: remainingVideos == int.MaxValue ? null : remainingVideos);
            }

            if (remainingMinutes >= requestedMinutes)
            {
                var afterMinutes = Math.Max(remainingMinutes - requestedMinutes, 0);
                return UsageDecision.Allowed(
                    remainingQuota: remainingVideos,
                    remainingTranscriptionMinutes: afterMinutes,
                    remainingVideos: remainingVideos);
            }

            if (remainingMinutes <= 0)
            {
                var message = "Лимит часов распознавания исчерпан. Пополните пакет в биллинге.";
                _logger.LogInformation(
                    "User {UserId} exceeded transcription minutes quota. RemainingMinutes={RemainingMinutes}, RequestedMinutes={RequestedMinutes}",
                    userId,
                    remainingMinutes,
                    requestedMinutes);

                return UsageDecision.Denied(
                    message,
                    billingUrl,
                    remainingQuota: remainingVideos,
                    remainingTranscriptionMinutes: remainingMinutes,
                    remainingVideos: remainingVideos,
                    requestedTranscriptionMinutes: requestedMinutes,
                    maxUploadMinutes: 0);
            }

            var deniedMessage =
                $"Файл длиннее доступного остатка: {requestedMinutes} мин при доступных {remainingMinutes} мин. " +
                $"Обрежьте файл до {remainingMinutes} мин или пополните пакет.";

            _logger.LogInformation(
                "User {UserId} attempted transcription beyond quota. RemainingMinutes={RemainingMinutes}, RequestedMinutes={RequestedMinutes}",
                userId,
                remainingMinutes,
                requestedMinutes);

            return UsageDecision.Denied(
                deniedMessage,
                billingUrl,
                remainingQuota: remainingVideos,
                remainingTranscriptionMinutes: remainingMinutes,
                remainingVideos: remainingVideos,
                requestedTranscriptionMinutes: requestedMinutes,
                maxUploadMinutes: remainingMinutes);
        }
    }
}
