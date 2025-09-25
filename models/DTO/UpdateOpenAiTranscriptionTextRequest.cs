namespace YandexSpeech.models.DTO
{
    public class UpdateOpenAiTranscriptionTextRequest
    {
        public string? RecognizedText { get; set; }

        public string? MarkdownText { get; set; }
    }
}
