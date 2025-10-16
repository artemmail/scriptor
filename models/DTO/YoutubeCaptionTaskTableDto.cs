using YandexSpeech.models.DB;

namespace YandexSpeech.models.DTO
{
    public class YoutubeCaptionTaskTableDto
    {
        public string Id { get; set; }
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }

        public string Error { get; set; }
        public string Slug { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public RecognizeStatus? Status { get; set; }
        public bool Done { get; set; }
        // Если нужно возвращать укороченное поле Result, можно добавить
        public string ResultShort { get; set; }
        public int SegmentsProcessed { get; set; }
        public int SegmentsTotal { get; set; }
        public DateTime? UploadDate { get; internal set; }
        public YoutubeCaptionVisibility Visibility { get; set; }
    }
}
