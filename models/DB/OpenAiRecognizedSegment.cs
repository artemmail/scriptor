using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("OpenAiRecognizedSegments")]
    public class OpenAiRecognizedSegment
    {
        [Key]
        public int SegmentId { get; set; }

        [Required]
        public string TaskId { get; set; } = default!;

        [ForeignKey(nameof(TaskId))]
        public OpenAiTranscriptionTask Task { get; set; } = default!;

        public int Order { get; set; }

        [Required]
        public string Text { get; set; } = default!;

        public string? ProcessedText { get; set; }

        public bool IsProcessed { get; set; }

        public bool IsProcessing { get; set; }

        public double? StartSeconds { get; set; }

        public double? EndSeconds { get; set; }
    }
}
