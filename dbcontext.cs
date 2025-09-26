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

        public DbSet<OpenAiTranscriptionTask> OpenAiTranscriptionTasks { get; set; }
        public DbSet<OpenAiTranscriptionStep> OpenAiTranscriptionSteps { get; set; }
        public DbSet<OpenAiRecognizedSegment> OpenAiRecognizedSegments { get; set; }


        // Новая таблица
        public DbSet<AudioFile> AudioFiles { get; set; }

        public DbSet<BlogTopic> BlogTopics { get; set; }
        public DbSet<BlogComment> BlogComments { get; set; }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<UserSubscription> UserSubscriptions { get; set; }
        public DbSet<SubscriptionInvoice> SubscriptionInvoices { get; set; }
        public DbSet<UserFeatureFlag> UserFeatureFlags { get; set; }
        public DbSet<UserWallet> UserWallets { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<PaymentOperation> PaymentOperations { get; set; }
        public DbSet<RecognitionUsage> RecognitionUsage { get; set; }

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

            builder.Entity<BlogComment>()
                .HasOne(c => c.CreatedBy)
                .WithMany()
                .HasForeignKey(c => c.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SubscriptionPlan>()
                .HasIndex(p => p.Code)
                .IsUnique();

            builder.Entity<SubscriptionPlan>()
                .Property(p => p.Price)
                .HasPrecision(18, 2);

            builder.Entity<UserSubscription>()
                .HasOne(us => us.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UserSubscription>()
                .HasOne(us => us.Plan)
                .WithMany(p => p.UserSubscriptions)
                .HasForeignKey(us => us.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UserSubscription>()
                .HasMany(us => us.Invoices)
                .WithOne(i => i.UserSubscription)
                .HasForeignKey(i => i.UserSubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserFeatureFlag>()
                .HasIndex(f => new { f.UserId, f.FeatureCode })
                .IsUnique();

            builder.Entity<UserFeatureFlag>()
                .HasOne(f => f.User)
                .WithMany(u => u.FeatureFlags)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserWallet>()
                .Property(w => w.Balance)
                .HasPrecision(18, 2);

            builder.Entity<UserWallet>()
                .HasOne(w => w.User)
                .WithOne(u => u.Wallet)
                .HasForeignKey<UserWallet>(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalletTransaction>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2);

            builder.Entity<WalletTransaction>()
                .HasOne(t => t.User)
                .WithMany(u => u.WalletTransactions)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PaymentOperation>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            builder.Entity<PaymentOperation>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RecognitionUsage>()
                .Property(r => r.ChargedAmount)
                .HasPrecision(18, 2);

            builder.Entity<RecognitionUsage>()
                .HasIndex(r => new { r.UserId, r.Date })
                .IsUnique();

            builder.Entity<RecognitionUsage>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ApplicationUser>()
                .HasOne(u => u.CurrentSubscription)
                .WithMany()
                .HasForeignKey(u => u.CurrentSubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
        }

    }
}
