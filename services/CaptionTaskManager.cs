using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using YandexSpeech.models.DB;
using YoutubeExplode.Videos; // for VideoId.TryParse

namespace YandexSpeech.services
{
    // ---------------- DTO -----------------
    public class YoutubeCaptionTaskDto
    {
        public string Id { get; set; } = default!;
        public string? VideoId { get; set; }
        public string? CaptionTrackKey { get; set; }
        public string? Title { get; set; }
        public string? ChannelName { get; set; }
        public string? ChannelId { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public int SegmentsTotal { get; set; }
        public int SegmentsProcessed { get; set; }
        public RecognizeStatus? Status { get; set; }
        public bool Done { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UploadDate { get; set; }
        public string? Slug { get; set; }
    }

    // -------------  INTERFACE -------------
    public interface ICaptionTaskManager
    {
        Task<string> EnqueueCaptionTaskAsync(string youtubeId, string createdBy, string userId, string? trackKey = null);
        Task<YoutubeCaptionTaskDto?> GetTaskStatusAsync(string taskId);
        Task<bool> DeleteTaskAsync(string taskId, string userId, bool isAdmin);
        Task<bool> UpdateTaskResultAsync(string taskId, string newResult, string userId, bool isAdmin);
        Task<YoutubeCaptionTaskDto?> RestartCaptionTaskAsync(string taskId, string userId, bool isAdmin);
        Task<List<YoutubeCaptionTask>> GetAllTasksAsync();
        Task ResumeIncompleteTasksAsync(CancellationToken ct = default);
        Task ProcessQueueAsync(CancellationToken ct = default);
    }

    // -------------  ENTITY UPDATE -------------
    // In your YandexSpeech.models.DB namespace, add:
    // public class RecognizedSegment
    // {
    //     public string YoutubeCaptionTaskId { get; set; } = default!;
    //     public int Order { get; set; }
    //     public string Text { get; set; } = default!;
    //     public string? ProcessedText { get; set; }
    //     public bool IsProcessed { get; set; }
    //     public bool IsProcessing { get; set; }   // new flag for atomic reservation
    // }

    // -------------  IMPLEMENTATION -------------
    public sealed class CaptionTaskManager : ICaptionTaskManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CaptionTaskManager> _logger;
        private readonly SemaphoreSlim _semaphore;

        // ID задач, выполняющихся прямо сейчас
        private static readonly ConcurrentDictionary<string, bool> _inProgress = new();

        private volatile bool _scanning;

        public CaptionTaskManager(IServiceScopeFactory scopeFactory,
                                  ILogger<CaptionTaskManager> logger,
                                  int maxConcurrent = 30)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }

        public async Task<string> EnqueueCaptionTaskAsync(
            string youtubeId, string createdBy, string userId, string? trackKey = null)
        {
            var vid = VideoId.TryParse(youtubeId);
            if (vid is not null)
                youtubeId = vid;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            var taskId = trackKey is null
                ? youtubeId
                : $"{youtubeId}-cc-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trackKey))).ToLowerInvariant()}";

            var existing = await db.YoutubeCaptionTasks
                                   .FirstOrDefaultAsync(t => t.Id == taskId);
            if (existing is not null)
            {
                // A shared video may have failed for a different user's empty balance.
                // A new request must use the requesting user's credits, not that old error.
                if (existing.Status == RecognizeStatus.Error
                    && !existing.QuotaChargedAt.HasValue
                    && string.IsNullOrWhiteSpace(existing.Result)
                    && existing.Error?.StartsWith("Недостаточно видео-кредитов для запуска задачи.", StringComparison.Ordinal) == true)
                {
                    existing.UserId = userId;
                    existing.IP = createdBy;
                    existing.Status = RecognizeStatus.Created;
                    existing.Done = false;
                    existing.Error = null;
                    existing.ModifiedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                // Re-enqueue unfinished tasks so "Created" items do not stay stuck forever.
                if (!existing.Done && existing.Status != RecognizeStatus.Error)
                {
                    _ = ProcessQueueAsync();
                }

                return existing.Id;
            }

            var newTask = new YoutubeCaptionTask
            {
                Id = taskId,
                VideoId = youtubeId,
                CaptionTrackKey = trackKey,
                IP = createdBy,
                Result = string.Empty,
                Status = RecognizeStatus.Created,
                Done = false,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Preview = string.Empty,
                SegmentsTotal = 0,
                SegmentsProcessed = 0,
                UserId = userId
            };

            db.YoutubeCaptionTasks.Add(newTask);
            await db.SaveChangesAsync();

            _ = ProcessQueueAsync();
            return newTask.Id;
        }

        public async Task<YoutubeCaptionTaskDto?> GetTaskStatusAsync(string taskId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            YoutubeCaptionTask? task = await db.YoutubeCaptionTasks
                .FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.PreviousSlug == taskId);
            }

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks.FindAsync(taskId);
            }

            if (task is null) return null;

            return new YoutubeCaptionTaskDto
            {
                Id = task.Id,
                VideoId = task.VideoId ?? task.Id,
                CaptionTrackKey = task.CaptionTrackKey,
                Slug = task.Slug ?? task.Id,
                Title = task.Title,
                UploadDate = task.UploadDate,
                ChannelName = task.ChannelName,
                ChannelId = task.ChannelId,
                Result = task.Result,
                Error = task.Error,
                SegmentsTotal = task.SegmentsTotal,
                SegmentsProcessed = task.SegmentsProcessed,
                Status = task.Status,
                Done = task.Done,
                ModifiedAt = task.ModifiedAt,
                CreatedAt = task.CreatedAt
            };
        }

        public async Task<bool> DeleteTaskAsync(string taskId, string userId, bool isAdmin)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Поиск задачи по Slug с последующим падением к ID
            var tasksWithDependents = db.YoutubeCaptionTasks
                .Include(t => t.RecognizedSegments)
                .Include(t => t.CaptionText);
            YoutubeCaptionTask? task = await tasksWithDependents
                .FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
            {
                task = await tasksWithDependents.FirstOrDefaultAsync(t => t.PreviousSlug == taskId);
            }

            if (task is null)
            {
                task = await tasksWithDependents.FirstOrDefaultAsync(t => t.Id == taskId);
            }

            if (task is null)
                return false; // Задача не найдена

            if (!isAdmin && task.UserId != userId)
                return false;

            // Удаляем связанные сегменты (если нужно обеспечить каскадное удаление вручную)
            if (task.RecognizedSegments?.Any() == true)
            {
                db.RecognizedSegments.RemoveRange(task.RecognizedSegments);
            }

            // Удаляем связанный текст субтитров (если не настроено каскадное удаление через EF)
            if (task.CaptionText != null)
            {
                db.YoutubeCaptionTexts.Remove(task.CaptionText);
            }

            // Удаляем саму задачу
            db.YoutubeCaptionTasks.Remove(task);

            // Сохраняем изменения
            await db.SaveChangesAsync();

            return true;
        }


        public async Task<bool> UpdateTaskResultAsync(string taskId, string newResult, string userId, bool isAdmin)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Поиск задачи по Slug с последующим падением к ID
            YoutubeCaptionTask? task = await db.YoutubeCaptionTasks
                .FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.PreviousSlug == taskId);
            }

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks.FindAsync(taskId);
            }

            if (task is null)
                return false; // Задача не найдена

            if (!isAdmin && task.UserId != userId)
                return false;

            // Обновляем результат и время модификации
            task.Result = newResult;
            task.ModifiedAt = DateTime.UtcNow;

            // Сохраняем изменения
            await db.SaveChangesAsync();

            return true;
        }

        public async Task<YoutubeCaptionTaskDto?> RestartCaptionTaskAsync(string taskId, string userId, bool isAdmin)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            YoutubeCaptionTask? task = await db.YoutubeCaptionTasks
                .FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks
                    .FirstOrDefaultAsync(t => t.PreviousSlug == taskId);
            }

            if (task is null)
            {
                task = await db.YoutubeCaptionTasks.FindAsync(taskId);
            }

            if (task is null)
                return null;

            if (!isAdmin && !string.Equals(task.UserId, userId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("User is not allowed to restart this task.");

            if (task.Status != RecognizeStatus.Error)
                return await GetTaskStatusAsync(task.Id);

            var stuckSegments = await db.RecognizedSegments
                .Where(s => s.YoutubeCaptionTaskId == task.Id && s.IsProcessing)
                .ToListAsync();

            foreach (var segment in stuckSegments)
            {
                segment.IsProcessing = false;
            }

            var hasSegments = await db.RecognizedSegments
                .AnyAsync(s => s.YoutubeCaptionTaskId == task.Id);
            var hasCaptionText = await db.YoutubeCaptionTexts
                .AnyAsync(t => t.Id == task.Id);

            task.Status = hasSegments
                ? RecognizeStatus.ApplyingPunctuationSegment
                : hasCaptionText
                    ? RecognizeStatus.SegmentingCaptions
                    : !string.IsNullOrWhiteSpace(task.Title) || task.UploadDate.HasValue || !string.IsNullOrWhiteSpace(task.ChannelId)
                        ? RecognizeStatus.DownloadingCaptions
                        : RecognizeStatus.Created;

            task.Done = false;
            task.Error = null;
            task.ModifiedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            _ = ProcessQueueAsync();
            return await GetTaskStatusAsync(task.Id);
        }


        public async Task<List<YoutubeCaptionTask>> GetAllTasksAsync()
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            return await db.YoutubeCaptionTasks.AsNoTracking().ToListAsync();
        }

        public async Task ResumeIncompleteTasksAsync(CancellationToken ct = default)
            => await ProcessQueueAsync(ct);

        public async Task ProcessQueueAsync(CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _scanning, true))
                return;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

                var pending = await db.YoutubeCaptionTasks
                                      .Where(t => t.Status != RecognizeStatus.Done &&
                                                  t.Status != RecognizeStatus.Error)
                                      .OrderBy(t => t.CreatedAt)
                                      .ToListAsync(ct);

                foreach (var task in pending)
                {
                    if (!_inProgress.TryAdd(task.Id, true))
                        continue;

                    await _semaphore.WaitAsync(ct);

                    // Один долгоживущий воркер на задачу
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var inner = _scopeFactory.CreateAsyncScope();
                            var service = inner.ServiceProvider.GetRequiredService<IYoutubeCaptionService>();

                            while (true)
                            {
                                var dto = await GetTaskStatusAsync(task.Id);
                                if (dto == null || dto.Done || dto.Status == RecognizeStatus.Error)
                                    break;

                                var statusBefore = dto.Status;
                                var segmentsBefore = dto.SegmentsProcessed;
                                var modifiedBefore = dto.ModifiedAt;

                                await service.ContinueCaptionTaskAsync(task.Id);

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
                                        "No progress detected for caption task {TaskId} at status {Status}. Scheduling retry in {DelaySeconds}s to avoid infinite loop.",
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
                            _logger.LogError(ex, "Error while processing task {TaskId}", task.Id);
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
                _logger.LogError(ex, "Unhandled error in ProcessQueueAsync");
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
                    _logger.LogError(ex, "Error while scheduling caption task queue retry");
                }
            });
        }
    }
}
