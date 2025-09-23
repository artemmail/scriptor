namespace YandexSpeech.models.DTO
{
    public class BlogTopicDto
    {
        public int Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int CommentCount { get; set; }
        public IReadOnlyCollection<BlogCommentDto> Comments { get; set; } = Array.Empty<BlogCommentDto>();
    }
}
