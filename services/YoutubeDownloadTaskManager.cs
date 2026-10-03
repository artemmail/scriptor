using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.services;
using YandexSpeech;
using YoutubeExplode.Videos;

using YoutubeDownload.Models;     // ваши модели
using YoutubeDownload.Services;   // YoutubeWorkflowService и т.д.

namespace YoutubeDownload.Managers
{
    public interface IYoutubeDownloadTaskManager
    {
        /// <summary>
        /// Добавить новую задачу (или вернуть существующую),
        /// а затем инициировать процесс её скачивания/слияния.
        /// </summary>
        Task<string> EnqueueDownloadAsync(
            string videoId,
            List<StreamDto> streamsToDownload,
            string createdBy
        );

        /// <summary>Получить статус задачи (из БД).</summary>
        Task<YoutubeDownloadTask?> GetTaskStatusAsync(string taskId, string userId);

        /// <summary>Обработать очередь (запустить задачи, которые ещё не выполнены).</summary>
        void ProcessQueue();

        /// <summary>Попробовать возобновить незавершённые задачи (после перезапуска приложения).</summary>
        Task ResumeIncompleteTasksAsync();
    }

    /// <summary>
    /// Аналог RecognitionTaskManager, но для YouTube загрузок и слияний.
    /// </summary>
    public class YoutubeDownloadTaskManager : IYoutubeDownloadTaskManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrentDownloads;

        /// <summary>
        /// Потокобезопасный словарь ID задач, которые уже обрабатываются,
        /// чтобы не запустить одну и ту же задачу дважды.
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> _inProgressTasks = new();

        public YoutubeDownloadTaskManager(IServiceScopeFactory scopeFactory, int maxConcurrentDownloads = 3)
        {
            _scopeFactory = scopeFactory;
            _maxConcurrentDownloads = maxConcurrentDownloads;
            _semaphore = new SemaphoreSlim(_maxConcurrentDownloads, _maxConcurrentDownloads);
        }

        /// <summary>
        /// Создаёт или возвращает существующую задачу (если такая уже есть и не завершена).
        /// После создания/нахождения задачи вызывает ProcessQueue(),
        /// чтобы попытаться запустить её на исполнение.
        /// </summary>
        public async Task<string> EnqueueDownloadAsync(
            string videoId,
            List<StreamDto> streamsToDownload,
            string userId
        )
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            var canonicalVideoId = VideoId.Parse(videoId).Value;

            // A task belongs to one user. Reusing another user's task would expose its id
            // and prevent the caller from creating their own download.
            var existingTask = await dbContext.YoutubeDownloadTasks.FirstOrDefaultAsync(x =>
                x.VideoId == canonicalVideoId && x.UserId == userId &&
                !x.Done && x.Status != YoutubeWorkflowStatus.Error
            );

            if (existingTask != null)
            {
                ProcessQueue();
                return existingTask.Id;
            }

            // Иначе создаём новую задачу
            var workflowService = scope.ServiceProvider.GetRequiredService<YoutubeWorkflowService>();
            // Запускаем "с нуля" (StartNewTaskAsync создаёт запись в БД).
            var newTask = await workflowService.StartNewTaskAsync(canonicalVideoId, streamsToDownload, userId);

            // Попробуем запустить
            ProcessQueue();

            return newTask.Id;
        }

        /// <summary>
        /// Возвращаем статус задачи из БД.
        /// </summary>
        public async Task<YoutubeDownloadTask?> GetTaskStatusAsync(string taskId, string userId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            return await dbContext.YoutubeDownloadTasks.AsNoTracking()
                .FirstOrDefaultAsync(task => task.Id == taskId && task.UserId == userId);
        }

        /// <summary>
        /// Запускает обработку очереди: ищем все незавершённые задачи и пытаемся их запустить (ContinueDownloadAsync).
        /// Ограничиваемся _maxConcurrentDownloads параллельных задач.
        /// </summary>
        public void ProcessQueue()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                    var workflowService = scope.ServiceProvider.GetRequiredService<YoutubeWorkflowService>();

                    // Список задач, которые не в Done и не в Error
                    var pendingTasks = await dbContext
                        .YoutubeDownloadTasks
                        .Where(x => x.Status != YoutubeWorkflowStatus.Done && x.Status != YoutubeWorkflowStatus.Error)
                        .OrderBy(x => x.CreatedAt)
                        .ToListAsync();

                    foreach (var taskEntity in pendingTasks)
                    {
                        // Проверяем, не идёт ли эта задача уже
                        if (!_inProgressTasks.TryAdd(taskEntity.Id, true))
                        {
                            // Значит, уже обрабатываем
                            continue;
                        }

                        // Пытаемся занять слот из семафора
                        if (await _semaphore.WaitAsync(0))
                        {
                            // Запускаем обработку в фоновом потоке
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var innerScope = _scopeFactory.CreateScope();
                                    var innerDbContext =
                                        innerScope.ServiceProvider.GetRequiredService<MyDbContext>();
                                    var innerWorkflowService =
                                        innerScope.ServiceProvider.GetRequiredService<YoutubeWorkflowService>();

                                    // Важно: берём задачу из innerDbContext (если нужно)
                                    var taskToProcess = await innerDbContext
                                        .YoutubeDownloadTasks
                                        .FindAsync(taskEntity.Id);

                                    if (taskToProcess != null)
                                    {
                                        await innerWorkflowService.ContinueDownloadAsync(taskToProcess.Id);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error in background download: {ex.Message}");
                                }
                                finally
                                {
                                    // Освобождаем слот семафора
                                    _semaphore.Release();
                                    // Удаляем задачу из "в процессе"
                                    _inProgressTasks.TryRemove(taskEntity.Id, out _);
                                }
                            });
                        }
                        else
                        {
                            // Нет свободных слотов — возвращаем задачу "в очередь"
                            _inProgressTasks.TryRemove(taskEntity.Id, out _);
                            // Выходим из цикла
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing queue: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// При запуске приложения можно вызвать,
        /// чтобы «подхватить» незавершённые задачи (Created/Downloading/Merging) и попробовать их доработать.
        /// </summary>
        public async Task ResumeIncompleteTasksAsync()
        {
            // Самый простой способ — заново вызвать ProcessQueue().
            ProcessQueue();
            await Task.CompletedTask;
        }
    }
}
