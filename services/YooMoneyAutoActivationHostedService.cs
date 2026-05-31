using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public sealed class YooMoneyAutoActivationHostedService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<YooMoneyAutoActivationHostedService> _logger;

        public YooMoneyAutoActivationHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<YooMoneyAutoActivationHostedService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("YooMoneyAutoActivationHostedService started.");

            await ProcessAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessAsync(stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task ProcessAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var autoActivationService = scope.ServiceProvider.GetRequiredService<IYooMoneyAutoActivationService>();
                await autoActivationService.ProcessAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in YooMoneyAutoActivationHostedService.");
            }
        }
    }
}
