using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB
{
    public class BlogComment
    {
        public int Id { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public int TopicId { get; set; }

        public BlogTopic Topic { get; set; } = null!;

        [Required]
        public string CreatedById { get; set; } = string.Empty;

        public ApplicationUser CreatedBy { get; set; } = null!;
    }
}
