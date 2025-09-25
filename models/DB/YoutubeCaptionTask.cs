using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum YoutubeCaptionVisibility
    {
        Public = 0,
        Hidden = 1,
        Deleted = 2
    }

    [Table("YoutubeCaptions")]
    public class YoutubeCaptionTask
    {
        [Key]
        public string Id { get; set; }

        public string? Title { get; set; }

        public string? ChannelName { get; set; }

        public string? ChannelId { get; set; }

        public RecognizeStatus? Status { get; set; }

        public bool Done { get; set; } = false;

        public DateTime? CreatedAt { get; set; }

        public DateTime? ModifiedAt { get; set; }

        public DateTime? UploadDate { get; set; }

        public string? Result { get; set; }

        public string? Error { get; set; }

        public string? Preview { get; set; }

        public int SegmentsTotal { get; set; }

        public int SegmentsProcessed { get; set; }

        /// <summary>
        /// Навигационное свойство для связанных сегментов.
        /// </summary>
        public ICollection<RecognizedSegment> RecognizedSegments { get; set; } = new List<RecognizedSegment>();

        /// <summary>
        /// Навигационное свойство для связанного текста субтитров.
        /// </summary>
        public YoutubeCaptionText CaptionText { get; set; }

        public string? Slug { get; set; }

        public string? IP { get; set; }
      
        [ForeignKey(nameof(User))]
        public string? UserId { get; set; }

        // Навигационное свойство к пользователю
        public virtual ApplicationUser? User { get; set; }

        public YoutubeCaptionVisibility Visibility { get; set; } = YoutubeCaptionVisibility.Public;

        public DateTime? VisibilityChangedAt { get; set; }
    }
}
