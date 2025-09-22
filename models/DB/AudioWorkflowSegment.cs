using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("AudioWorkflowSegments")]
    public class AudioWorkflowSegment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string TaskId { get; set; }
        [ForeignKey(nameof(TaskId))]
        public virtual AudioWorkflowTask Task { get; set; }

        public int Order { get; set; }

        [Required]
        public string Text { get; set; }

        public string? ProcessedText { get; set; }

        public bool IsProcessing { get; set; }
        public bool IsProcessed { get; set; }
    }
}
