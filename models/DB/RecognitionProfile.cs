using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("RecognitionProfiles")]
    public class RecognitionProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Request { get; set; } = string.Empty;

        public string? ClarificationTemplate { get; set; }

        [Required]
        [MaxLength(200)]
        public string OpenAiModel { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int SegmentBlockSize { get; set; }
    }
}
