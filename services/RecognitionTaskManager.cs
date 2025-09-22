using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YoutubeExplode.Videos;

namespace YandexSpeech.services
{
    public class RecognitionTaskManager : IRecognitionTaskManager
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrentTasks;

        /// <summary>
        /// Потокобезопасный набор ID задач, которые уже в процессе обработки,
        /// чтобы не запускать их повторно.
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> _inProgressTasks = new();

        public RecognitionTaskManager(IServiceScopeFactory scopeFactory, int maxConcurrentTasks = 3)
        {
            _scopeFactory = scopeFactory;
            _maxConcurrentTasks = maxConcurrentTasks;
            _semaphore = new SemaphoreSlim(_maxConcurrentTasks, _maxConcurrentTasks);
        }

        /// <summary>
        /// Ставим в очередь обычную задачу распознавания (локальный файл).
        /// </summary>
        public async Task<string> EnqueueRecognitionAsync(string filePath, string createdBy)
        {
            using var scope = _scopeFactory.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Проверяем, нет ли уже незавершённой задачи для этого файла
            var existingTask = await _dbContext.SpeechRecognitionTasks
                .FirstOrDefaultAsync(x =>
                    x.OriginalFilePath == filePath &&
                    !x.Done &&
                    !x.IsSubtitleTask // уточняем, что это НЕ задача субтитров
                );

            if (existingTask != null)
            {
                // Уже есть активная / незавершённая задача
                return existingTask.Id;
            }

            // Иначе создаём новую
            var newTask = new SpeechRecognitionTask
            {
                Id = Guid.NewGuid().ToString(),
                OriginalFilePath = filePath,
                BucketName = "ruticker",
                CreatedBy = createdBy,
                Status = RecognizeStatus.Created,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Done = false,

                // Флаги по умолчанию
                IsSubtitleTask = false,
                YoutubeId = null,
                Language = null
            };

            try
            {
                _dbContext.SpeechRecognitionTasks.Add(newTask);
                await _dbContext.SaveChangesAsync();

                // Запускаем процесс в очередь
                ProcessQueue();
            }
            catch (Exception ex)
            {
                // Логируем при необходимости
                Console.WriteLine(ex.Message);
            }

            return newTask.Id;
        }

        /// <summary>
        /// Ставим в очередь задачу субтитров (YouTube).
        /// </summary>
        public async Task<string> EnqueueSubtitleRecognitionAsync(
            string youtubeId,
            string? language,
            string createdBy)
        {
             youtubeId = VideoId.Parse(youtubeId);

            using var scope = _scopeFactory.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            // Проверяем, нет ли уже незавершённой задачи субтитров для этого youtubeId
            var existingTask = await _dbContext.SpeechRecognitionTasks
                .FirstOrDefaultAsync(x =>
                    x.YoutubeId == youtubeId &&
                    x.IsSubtitleTask &&
                    !x.Done
                );

            if (existingTask != null)
            {
                // Уже есть активная / незавершённая задача субтитров
                return existingTask.Id;
            }

            // Иначе создаём новую
            var newTask = new SpeechRecognitionTask
            {
                Id = Guid.NewGuid().ToString(),
                BucketName = "ruticker",
                CreatedBy = createdBy,
                Status = RecognizeStatus.Created,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Done = false,

                IsSubtitleTask = true,
                YoutubeId = youtubeId,
                Language = language
            };

            try
            {
                _dbContext.SpeechRecognitionTasks.Add(newTask);
                await _dbContext.SaveChangesAsync();

                // Запускаем процесс
                ProcessQueue();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return newTask.Id;
        }

        /// <summary>
        /// Получить статус задачи из БД.
        /// </summary>
        public async Task<SpeechRecognitionTask> GetTaskStatusAsync(string taskId)
        {
            using var scope = _scopeFactory.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            return await _dbContext.SpeechRecognitionTasks.FindAsync(taskId);
        }


        public async Task<List<SpeechRecognitionTask>> GetAllTasksAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            return await dbContext.SpeechRecognitionTasks.ToListAsync();
        }


        /// <summary>
        /// Вызывается либо вручную, либо из фоновой службы.
        /// Пробует запустить все непройденные задачи (status != Done/Error),
        /// пока не достигнем лимита в _maxConcurrentTasks.
        /// </summary>
        public void ProcessQueue()
        {
            // Запуск обработки очереди в фоновом потоке
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedDbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

                    var pendingTasks = await scopedDbContext
                        .SpeechRecognitionTasks
                        .Where(x => x.Status != RecognizeStatus.Done && x.Status != RecognizeStatus.Error)
                        .OrderBy(x => x.CreatedAt)
                        .ToListAsync();

                    foreach (var taskEntity in pendingTasks)
                    {
                        // Проверяем, не идёт ли уже эта задача в обработке
                        if (!_inProgressTasks.TryAdd(taskEntity.Id, true))
                        {
                            // Значит, задача уже в процессе
                            continue;
                        }

                        // Пытаемся получить слот из семафора
                        if (await _semaphore.WaitAsync(0))
                        {
                            // Запускаем обработку в фоне
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var innerScope = _scopeFactory.CreateScope();
                                    var innerScopedDbContext =
                                        innerScope.ServiceProvider.GetRequiredService<MyDbContext>();
                                    var workflowService =
                                        innerScope.ServiceProvider.GetRequiredService<ISpeechWorkflowService>();

                                    // Загружаем задачу из БД в этом скоупе
                                    var task = await innerScopedDbContext.SpeechRecognitionTasks
                                        .FindAsync(taskEntity.Id);

                                    if (task != null)
                                    {
                                        // Запускаем (или продолжаем) Workflow
                                        await workflowService.ContinueRecognitionAsync(task.Id);
                                    }
                                }
                                finally
                                {
                                    // Освобождаем слот семафора
                                    _semaphore.Release();
                                    // Убираем задачу из "в процессе"
                                    _inProgressTasks.TryRemove(taskEntity.Id, out _);
                                }
                            });
                        }
                        else
                        {
                            // Нет свободного слота
                            _inProgressTasks.TryRemove(taskEntity.Id, out _);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Логируем ошибку
                    Console.WriteLine($"Error processing queue: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Возобновить незавершённые задачи (например, после рестарта приложения).
        /// </summary>
        public async Task ResumeIncompleteTasksAsync()
        {
            // Простая реализация — просто перекидываемся на ProcessQueue
            ProcessQueue();
            await Task.CompletedTask;
        }
    }
}
