using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YoutubeDownload.Models
{
    [Table("YoutubeDownloadSteps")]
    public class YoutubeDownloadStep
    {
        [Key]
        public int StepId { get; set; }

        /// <summary>
        /// Связь с основной задачей
        /// </summary>
        [Required]
        [ForeignKey("DownloadTask")]
        public string TaskId { get; set; }

        public virtual YoutubeDownloadTask DownloadTask { get; set; }

        /// <summary>
        /// Какой конкретно шаг (см. enum YoutubeWorkflowStatus)
        /// </summary>
        [Required]
        public YoutubeWorkflowStatus Step { get; set; }

        /// <summary>
        /// Когда начался шаг
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// Когда завершился шаг (если завершился)
        /// </summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>
        /// Статус (успешно/ошибка). Можно использовать тот же enum YoutubeWorkflowStatus
        /// или свой отдельный статус для шага.
        /// </summary>
        public YoutubeWorkflowStatus? Status { get; set; }

        /// <summary>
        /// Сообщение об ошибке, если шаг не удался
        /// </summary>
        public string? Error { get; set; }
    }
}
