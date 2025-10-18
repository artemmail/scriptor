using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.services;

namespace YandexSpeech.services
{
    // DTO for controller responses
    public class AudioWorkflowTaskDto
    {
        public string Id { get; set; } = default!;
        public string AudioFileId { get; set; } = default!;
        public RecognizeStatus Status { get; set; }
        public bool Done { get; set; }
        public int SegmentsTotal { get; set; }
        public int SegmentsProcessed { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    // Interface defining queue manager
    public interface IAudioTaskManager
    {
        Task<string> EnqueueRecognitionTaskAsync(string fileId, string createdBy);
        Task<AudioWorkflowTaskDto?> GetTaskStatusAsync(string taskId);
        Task<bool> DeleteTaskAsync(string taskId);
        Task<List<AudioWorkflowTaskDto>> GetAllTasksAsync();
        Task ResumeIncompleteTasksAsync(CancellationToken ct = default);
        Task ProcessQueueAsync(CancellationToken ct = default);
    }

    // Implementation of the queue manager
    public sealed class AudioTaskManager : IAudioTaskManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AudioTaskManager> _logger;
        private readonly SemaphoreSlim _semaphore;
        private static readonly ConcurrentDictionary<string, bool> _inProgress = new();
        private volatile bool _scanning;

        public AudioTaskManager(IServiceScopeFactory scopeFactory,
                                ILogger<AudioTaskManager> logger,
                                int maxConcurrent = 10)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public async Task<string> EnqueueRecognitionTaskAsync(string fileId, string createdBy)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            var speechSvc = scope.ServiceProvider.GetRequiredService<ISpeechWorkflowService>();

            // Start or retrieve existing
            var task = await speechSvc.StartRecognitionTaskAsync(fileId, createdBy);
            _ = ProcessQueueAsync();
            return task.Id;
        }

        public async Task<AudioWorkflowTaskDto?> GetTaskStatusAsync(string taskId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            var task = await db.AudioWorkflowTasks
                               .AsNoTracking()
                               .FirstOrDefaultAsync(t => t.Id == taskId);
            if (task == null)
                return null;

            return new AudioWorkflowTaskDto
            {
                Id = task.Id,
                AudioFileId = task.AudioFileId,
                Status = task.Status,
                Done = task.Done,
                SegmentsTotal = task.SegmentsTotal,
                SegmentsProcessed = task.SegmentsProcessed,
                Result = task.Result,
                Error = task.Error,
                CreatedAt = task.CreatedAt,
                ModifiedAt = task.ModifiedAt
            };
        }

        public async Task<bool> DeleteTaskAsync(string taskId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            var task = await db.AudioWorkflowTasks.FindAsync(taskId);
            if (task == null)
                return false;

            db.AudioWorkflowTasks.Remove(task);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<List<AudioWorkflowTaskDto>> GetAllTasksAsync()
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            var tasks = await db.AudioWorkflowTasks
                                 .AsNoTracking()
                                 .OrderByDescending(t => t.CreatedAt)
                                 .ToListAsync();
            return tasks.Select(t => new AudioWorkflowTaskDto
            {
                Id = t.Id,
                AudioFileId = t.AudioFileId,
                Status = t.Status,
                Done = t.Done,
                SegmentsTotal = t.SegmentsTotal,
                SegmentsProcessed = t.SegmentsProcessed,
                Result = t.Result,
                Error = t.Error,
                CreatedAt = t.CreatedAt,
                ModifiedAt = t.ModifiedAt
            }).ToList();
        }

        public async Task ResumeIncompleteTasksAsync(CancellationToken ct = default)
            => await ProcessQueueAsync(ct);

        public async Task ProcessQueueAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _scanning, true))
                return;

            try
            {
                // 1) Зарезолвили только список pending–тасков
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                var pending = await db.AudioWorkflowTasks
                                      .Where(t => !t.Done && t.Status != RecognizeStatus.Error)
                                      .OrderBy(t => t.CreatedAt)
                                      .ToListAsync(ct);

                foreach (var task in pending)
                {
                    if (!_inProgress.TryAdd(task.Id, true))
                        continue;

                    await _semaphore.WaitAsync(ct);

                    // 2) Запускаем воркер, который внутри себя откроет свой scope
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (true)
                            {
                                var dto = await GetTaskStatusAsync(task.Id);
                                if (dto == null || dto.Done || dto.Status == RecognizeStatus.Error)
                                    break;

                                var statusBefore = dto.Status;
                                var segmentsBefore = dto.SegmentsProcessed;
                                var modifiedBefore = dto.ModifiedAt;

                                // 3) Для каждого шага создаём новый scope
                                await using var workerScope = _scopeFactory.CreateAsyncScope();
                                var speechSvcWorker =
                                    workerScope.ServiceProvider.GetRequiredService<ISpeechWorkflowService>();

                                await speechSvcWorker.ContinueRecognitionAsync(task.Id);

                                var updated = await GetTaskStatusAsync(task.Id);
                                if (updated == null || updated.Done || updated.Status == RecognizeStatus.Error)
                                    break;

                                var progressDetected =
                                    updated.Status != statusBefore
                                    || updated.SegmentsProcessed != segmentsBefore
                                    || updated.ModifiedAt > modifiedBefore;

                                if (!progressDetected)
                                {
                                    var delay = TimeSpan.FromSeconds(5);
                                    _logger.LogWarning(
                                        "No progress detected for audio task {TaskId} at status {Status}. Scheduling retry in {DelaySeconds}s to avoid infinite loop.",
                                        task.Id,
                                        updated.Status,
                                        delay.TotalSeconds);

                                    ScheduleQueueRetry(delay);
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing audio task {TaskId}", task.Id);
                        }
                        finally
                        {
                            _inProgress.TryRemove(task.Id, out _);
                            _semaphore.Release();
                        }
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AudioTaskManager.ProcessQueueAsync");
            }
            finally
            {
                _scanning = false;
            }
        }

        private void ScheduleQueueRetry(TimeSpan delay)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, CancellationToken.None);
                    await ProcessQueueAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while scheduling audio task queue retry");
                }
            });
        }

    }

    // Extension to register in DI
    public static class AudioTaskManagerExtensions
    {
        public static IServiceCollection AddAudioTaskManager(this IServiceCollection services)
        {
            services.AddSingleton<IAudioTaskManager, AudioTaskManager>();
            return services;
        }
    }
}
