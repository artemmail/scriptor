using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YandexSpeech.models.DB;

namespace YoutubeDownload.Models
{
    [Table("YoutubeDownloadTasks")]
    public class YoutubeDownloadTask
    {
        [Key]
        public string Id { get; set; }

        /// <summary>Идентификатор или URL видео на YouTube.</summary>
        [Required]
        public string VideoId { get; set; }

        public string? Title { get; set; }


        /// <summary>Дата создания задачи.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Дата последнего изменения.</summary>
        public DateTime? ModifiedAt { get; set; }

        /// <summary>Текущий статус многошагового процесса.</summary>
        public YoutubeWorkflowStatus? Status { get; set; }

        /// <summary>Флаг завершения задачи (дополнительно к Status = Done).</summary>
        public bool Done { get; set; }

        /// <summary>Текст/описание ошибки (если упало).</summary>
        public string? Error { get; set; }

        /// <summary>Список потоков, которые нужно скачать (в JSON).  
        /// Или можно вынести в отдельную таблицу.  
        /// </summary>
        public string? StreamsJson { get; set; }

        /// <summary>Итоговый путь к слитому файлу (после Merging).  
        /// Или можно хранить в YoutubeDownloadFiles.</summary>
        public string? MergedFilePath { get; set; }

        /// <summary>Связанные файлы (каждый скачанный поток).  
        /// </summary>
        public virtual ICollection<YoutubeDownloadFile>? Files { get; set; }


        [ForeignKey("Channel")]
    public string? ChannelId { get; set; }
    public virtual YoutubeChannel? Channel { get; set; }

        [ForeignKey(nameof(User))]
        public string? UserId { get; set; }

        // Навигационное свойство к пользователю
        public virtual ApplicationUser? User { get; set; }
    }
}
