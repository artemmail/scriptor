using YandexSpeech.services;

namespace YandexSpeech.Services;

public sealed class RecognitionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecognitionBackgroundService> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(1);

    public RecognitionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecognitionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _logger.LogInformation("RecognitionBackgroundService started.");

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var mgr = scope.ServiceProvider.GetRequiredService<ICaptionTaskManager>();
            await mgr.ResumeIncompleteTasksAsync(stop);
        }

        using var timer = new PeriodicTimer(_interval);   // ← без await using

        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var mgr = scope.ServiceProvider.GetRequiredService<ICaptionTaskManager>();
                await mgr.ProcessQueueAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in RecognitionBackgroundService.");
            }
        }

        _logger.LogInformation("RecognitionBackgroundService stopped.");
    }

}
