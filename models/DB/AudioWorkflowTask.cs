using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("AudioWorkflowTasks")]
    public class AudioWorkflowTask
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // FK на только что загруженный/конвертированный аудиофайл
        [Required]
        public string AudioFileId { get; set; }
        [ForeignKey(nameof(AudioFileId))]
        public virtual AudioFile AudioFile { get; set; }

        // Для загрузки в облако
        [Required]
        public string BucketName { get; set; }
        public string? ObjectKey { get; set; }

        // Для Yandex Speech API
        public string? OperationId { get; set; }
        public string? RecognizedText { get; set; }

        // Итог и превью
        public string? Result { get; set; }
        public string? Preview { get; set; }

        // Статус и ошибки
        public RecognizeStatus Status { get; set; }
        public bool Done { get; set; }
        public string? Error { get; set; }

        // Метаданные
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        [Required]
        public string CreatedBy { get; set; }

        // Прогресс сегментов
        public int SegmentsTotal { get; set; }
        public int SegmentsProcessed { get; set; }

        public virtual ICollection<AudioWorkflowSegment> AudioWorkflowSegments { get; set; }
            = new List<AudioWorkflowSegment>();
    }
}
