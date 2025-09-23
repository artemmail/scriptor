using System.ComponentModel.DataAnnotations;
using YandexSpeech.models.DB;

namespace YandexSpeech.models.DB
{
    public class BlogTopic
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        [Required]
        public string CreatedById { get; set; } = string.Empty;

        public ApplicationUser CreatedBy { get; set; } = null!;

        public ICollection<BlogComment> Comments { get; set; } = new List<BlogComment>();
    }
}
