using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.services
{
    public class SubscriptionExpirationHostedService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<SubscriptionExpirationHostedService> _logger;
        private readonly TimeSpan _interval;

        public SubscriptionExpirationHostedService(
            IServiceProvider services,
            IOptions<SubscriptionLimitsOptions> options,
            ILogger<SubscriptionExpirationHostedService> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _interval = (options?.Value ?? new SubscriptionLimitsOptions()).GetExpirationInterval();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessExpiredSubscriptionsAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process expired subscriptions");
                }

                try
                {
                    await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ProcessExpiredSubscriptionsAsync(CancellationToken cancellationToken)
        {
            await using var scope = _services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

            var now = DateTime.UtcNow;

            var expired = await dbContext.UserSubscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate != null && s.EndDate <= now)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (expired.Count == 0)
            {
                return;
            }

            foreach (var subscription in expired)
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.CancelledAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var subscription in expired)
            {
                try
                {
                    await subscriptionService.RefreshUserCapabilitiesAsync(subscription.UserId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh capabilities for expired subscription {SubscriptionId}", subscription.Id);
                }
            }

            _logger.LogInformation("Expired {Count} subscriptions", expired.Count);
        }
    }
}
