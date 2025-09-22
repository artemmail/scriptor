using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB
{
    [Table("YoutubeStreamCaches")]
    public class YoutubeStreamCache
    {
        [Key]
        public string VideoId { get; set; } = null!;

        /// <summary>JSON со списком StreamDto</summary>
        public string StreamsJson { get; set; } = null!;

        /// <summary>Когда закешировано</summary>
        public DateTime RetrievedAt { get; set; }
    }
}
