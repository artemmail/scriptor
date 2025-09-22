using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public interface IRecognitionTaskManager
    {
        /// <summary>
        /// Добавить новую задачу на распознавание (если не существует).
        /// Возвращает Id задачи (новой или существующей).
        /// </summary>
        Task<string> EnqueueRecognitionAsync(string filePath, string createdBy);

        /// <summary>
        /// Проверка статуса задачи.
        /// </summary>
        Task<SpeechRecognitionTask> GetTaskStatusAsync(string taskId);

        Task<List<SpeechRecognitionTask>> GetAllTasksAsync();

        /// <summary>
        /// Инициировать обработку (если есть свободные слоты).
        /// Можно вызывать из фоновой службы или вручную.
        /// </summary>
        void ProcessQueue();

        /// <summary>
        /// При старте приложения восстановить незавершённые задачи и повторить их выполнение.
        /// </summary>
        Task ResumeIncompleteTasksAsync();

        Task<string> EnqueueSubtitleRecognitionAsync(string youtubeId, string? language, string createdBy);

    }
}
