using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB
{
    [Table("RecognizedSegments")]
    public class RecognizedSegment
    {
        [Key]
        public int SegmentId { get; set; }

        [Required]
        public string YoutubeCaptionTaskId { get; set; } = default!;

        [ForeignKey("YoutubeCaptionTaskId")]
        public YoutubeCaptionTask YoutubeCaptionTask { get; set; } = default!;

        public int Order { get; set; }

        [Required]
        public string Text { get; set; } = default!;

        public string? ProcessedText { get; set; }

        public bool IsProcessed { get; set; } = false;

        // Flag to indicate a segment is currently being processed
        public bool IsProcessing { get; set; } = false;
    }
}
