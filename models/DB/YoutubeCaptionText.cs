using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("YoutubeCaptionTexts")]
    public class YoutubeCaptionText
    {
        [Key, ForeignKey("YoutubeCaptionTask")]
        public string Id { get; set; }

        [Required]
        public string Caption { get; set; }

        // Навигационное свойство обратно к задаче
        public YoutubeCaptionTask YoutubeCaptionTask { get; set; }
    }
}
