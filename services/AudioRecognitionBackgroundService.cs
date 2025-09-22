using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.services;

namespace YandexSpeech.Services
{
    public sealed class AudioRecognitionBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AudioRecognitionBackgroundService> _logger;
        private static readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

        public AudioRecognitionBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AudioRecognitionBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AudioRecognitionBackgroundService started.");

            // При старте «поднимаем» всё, что осталось незавершённым
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var audioMgr = scope.ServiceProvider.GetRequiredService<IAudioTaskManager>();
                await audioMgr.ResumeIncompleteTasksAsync(stoppingToken);
            }

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var audioMgr = scope.ServiceProvider.GetRequiredService<IAudioTaskManager>();
                    await audioMgr.ProcessQueueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // нормально выходим
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in AudioRecognitionBackgroundService.");
                }
            }

            _logger.LogInformation("AudioRecognitionBackgroundService stopped.");
        }
    }
}
