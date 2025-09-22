using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using YandexSpeech.models.DB;
using YoutubeExplode.Videos; // for VideoId.TryParse

namespace YandexSpeech.services
{
    // ---------------- DTO -----------------
    public class YoutubeCaptionTaskDto
    {
        public string Id { get; set; } = default!;
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
    }

    // -------------  INTERFACE -------------
    public interface ICaptionTaskManager
    {
        Task<string> EnqueueCaptionTaskAsync(string youtubeId, string createdBy, string userId);
        Task<YoutubeCaptionTaskDto?> GetTaskStatusAsync(string taskId);
        Task<bool> DeleteTaskAsync(string taskId);
        Task<bool> UpdateTaskResultAsync(string taskId, string newResult);
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

        public async Task<string> EnqueueCaptionTaskAsync(string youtubeId, string createdBy, string userId)
        {
            var vid = VideoId.TryParse(youtubeId);
            if (vid is not null)
                youtubeId = vid;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            var existing = await db.YoutubeCaptionTasks
                                   .FirstOrDefaultAsync(t => t.Id == youtubeId);// && !t.Done);
            if (existing is not null)
                return existing.Id;

            var newTask = new YoutubeCaptionTask
            {
                Id = youtubeId,
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

            YoutubeCaptionTask? task =
                taskId.Length == 11
                    ? await db.YoutubeCaptionTasks.FindAsync(taskId)
                    : await db.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null) return null;

            return new YoutubeCaptionTaskDto
            {
                Id = task.Id,
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

        public async Task<bool> DeleteTaskAsync(string taskId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Поиск задачи по ID (length==11) или по Slug
            YoutubeCaptionTask? task =
                taskId.Length == 11
                    ? await db.YoutubeCaptionTasks.FindAsync(taskId)
                    : await db.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
                return false; // Задача не найдена

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


        public async Task<bool> UpdateTaskResultAsync(string taskId, string newResult)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Поиск задачи по ID (length==11) или по Slug
            YoutubeCaptionTask? task =
                taskId.Length == 11
                    ? await db.YoutubeCaptionTasks.FindAsync(taskId)
                    : await db.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Slug == taskId);

            if (task is null)
                return false; // Задача не найдена

            // Обновляем результат и время модификации
            task.Result = newResult;
            task.ModifiedAt = DateTime.UtcNow;

            // Сохраняем изменения
            await db.SaveChangesAsync();

            return true;
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

                                await service.ContinueCaptionTaskAsync(task.Id);
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
    }
}
