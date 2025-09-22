namespace YoutubeDownload.Models
{
    public enum YoutubeWorkflowStatus
    {
        Created = 0,    // Задача создана
        Downloading = 1,// Скачиваем потоки (видео/аудио)
        Merging = 2,    // Слияние видео+аудио
        Done = 3,       // Завершено
        Error = 4       // Ошибка
    }
}