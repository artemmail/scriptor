using YandexSpeech.models.DB;

namespace YandexSpeech.models.DTO
{
    public class SpeechRecognitionTaskDto
    {
        public string Id { get; set; }
        public RecognizeStatus? Status { get; set; }
        public bool Done { get; set; } = false;
        public DateTime? CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public string? YoutubeId { get; set; }
        public string? Language { get; set; }

        /// <summary>
        /// Конструктор для копирования данных из оригинальной структуры SpeechRecognitionTask
        /// </summary>
        public SpeechRecognitionTaskDto(SpeechRecognitionTask task)
        {
            Id = task.Id;
            Status = task.Status;
            Done = task.Done;
            CreatedAt = task.CreatedAt;
            CreatedBy = task.CreatedBy;
            Result = task.Result;
            Error = task.Error;
            YoutubeId = task.YoutubeId;
            Language = task.Language;
        }
    }
}
