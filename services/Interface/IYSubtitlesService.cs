using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;

namespace YandexSpeech.services
{

    public class YoutubeCaptionTaskTableDto1
    {
        /// <summary>
        /// Название канала.
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// Дата создания.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Заголовок.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Slug для формирования ссылки (если отсутствует, используется Id).
        /// </summary>
        public string Slug { get; set; }
        public DateTime? UploadDate { get; internal set; }
    }

    public interface IYSubtitlesService
    {
        // -----------------------------
        // Утилитные методы
        // -----------------------------
        /// <summary>Транслитерирует русскую строку в латиницу</summary>
        string Transliterate(string text);

        /// <summary>Удаляет Markdown-разметку из текста, оставляя "голый" текст</summary>
        string RemoveMarkdown(string markdown);

        /// <summary>Генерирует slug на основе заголовка и списка уже существующих slug-ов</summary>
        string GenerateSlug(string header, IEnumerable<string> existingSlugs);

        public string GenerateSlug(string header);

        // -----------------------------
        // Методы для работы с БД
        // -----------------------------
        /// <summary>Вернуть все задачи (без пагинации)</summary>
        Task<List<YoutubeCaptionTask>> GetAllTasksAsync();

        /// <summary>Вернуть одну задачу по Id</summary>
        Task<YoutubeCaptionTask> GetTaskByIdAsync(string taskId);

        /// <summary>Обновить "пустые" Title при необходимости</summary>
        

        Task PopulateSlugsAsync();

       Task<bool> NotifyYandexAsync(string domain, string key, string slug);
        Task<List<YoutubeCaptionTaskTableDto1>> GetAllTasksTableAsync();

        /// <summary>
        /// Возвращает задачи постранично, с учётом фильтрации и сортировки.
        /// </summary>
        Task<(List<YoutubeCaptionTaskTableDto> Items, int TotalCount)> GetTasksPagedAsync(
            int page,
            int pageSize,
            string sortField,
            string sortOrder,
            string filter,
            string userId = null);
    }
}
