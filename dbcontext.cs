using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YoutubeDownload.Models;

namespace YandexSpeech
{
    public class MyDbContext : IdentityDbContext<ApplicationUser>
    {
        public MyDbContext(DbContextOptions<MyDbContext> options)
            : base(options) { }

        // DbSet для ваших сущностей
        public DbSet<RecognizeResultDB> MyEntities { get; set; }
        public DbSet<SpeechRecognitionTask> SpeechRecognitionTasks { get; set; }

        public DbSet<YoutubeDownloadFile> YoutubeDownloadFiles { get; set; }

        public DbSet<YoutubeDownloadStep> YoutubeDownloadSteps { get; set; }

        public DbSet<YoutubeDownloadTask> YoutubeDownloadTasks { get; set; }

        public DbSet<YoutubeCaptionTask> YoutubeCaptionTasks { get; set; }

        
        public DbSet<RecognizedSegment> RecognizedSegments { get; set; }

        public DbSet<YoutubeCaptionText> YoutubeCaptionTexts { get; set; }

        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<AudioWorkflowTask> AudioWorkflowTasks { get; set; }
        public DbSet<AudioWorkflowSegment> AudioWorkflowSegments { get; set; }

        public DbSet<YoutubeChannel> YoutubeChannels { get; set; }
        public DbSet<YoutubeStreamCache> YoutubeStreamCaches { get; set; }


        // Новая таблица
        public DbSet<AudioFile> AudioFiles { get; set; }

        public DbSet<BlogTopic> BlogTopics { get; set; }
        public DbSet<BlogComment> BlogComments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BlogTopic>()
                .HasIndex(t => t.Slug)
                .IsUnique();

            builder.Entity<BlogTopic>()
                .HasMany(t => t.Comments)
                .WithOne(c => c.Topic)
                .HasForeignKey(c => c.TopicId)
                .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
