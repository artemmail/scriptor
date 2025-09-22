using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("AudioFiles")]
    public class AudioFile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string OriginalFileName { get; set; }

        [Required]
        public string OriginalFilePath { get; set; }

        public string? ConvertedFileName { get; set; }
        public string? ConvertedFilePath { get; set; }

        [Required]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ConvertedAt { get; set; }

        // Пользователь, загрузивший файл
        [Required]
        public string CreatedBy { get; set; }

        // Связь с задачей распознавания
        public string? SpeechRecognitionTaskId { get; set; }
        [ForeignKey("SpeechRecognitionTaskId")]
        public virtual SpeechRecognitionTask? SpeechRecognitionTask { get; set; }
    }
}
