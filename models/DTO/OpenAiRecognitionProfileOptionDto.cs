namespace YandexSpeech.models.DTO
{
    public class OpenAiRecognitionProfileOptionDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string DisplayedName { get; set; } = string.Empty;

        public string? ClarificationTemplate { get; set; }
    }
}
