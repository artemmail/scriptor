namespace YandexSpeech.models.DTO
{
    public class RecognitionProfileDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayedName { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public string? ClarificationTemplate { get; set; }
        public string OpenAiModel { get; set; } = string.Empty;
        public int SegmentBlockSize { get; set; }
    }

    public class UpsertRecognitionProfileRequest
    {
        public string? Name { get; set; }
        public string? DisplayedName { get; set; }
        public string? Request { get; set; }
        public string? ClarificationTemplate { get; set; }
        public string? OpenAiModel { get; set; }
        public int? SegmentBlockSize { get; set; }
    }
}
