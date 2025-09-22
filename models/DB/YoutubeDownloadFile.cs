using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YoutubeDownload.Models
{
    [Table("YoutubeDownloadFiles")]
    public class YoutubeDownloadFile
    {
        [Key]
        public int FileId { get; set; }

        [ForeignKey("DownloadTask")]
        public string TaskId { get; set; }
        public virtual YoutubeDownloadTask? DownloadTask { get; set; }

        /// <summary>Тип потока: "audio"/"video"/"muxed" — 
        /// при желании можно сделать enum, но достаточно и строки.</summary>
        public string? StreamType { get; set; }

        /// <summary>Качество (например, "720p") или null (для аудио).</summary>
        public string? QualityLabel { get; set; }

        /// <summary>Контейнер: "mp4", "webm" и т.д.</summary>
        public string? Container { get; set; }

        /// <summary>Язык дорожки (для аудио), может быть null.</summary>
        public string? Language { get; set; }

        /// <summary>Путь к скачанному файлу.</summary>
        public string? FilePath { get; set; }

        /// <summary>Дата, когда скачали этот файл.</summary>
        public DateTime CreatedAt { get; set; }
    }
}
