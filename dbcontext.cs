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

        public DbSet<RecognitionProfile> RecognitionProfiles { get; set; }


        // Новая таблица
        public DbSet<AudioFile> AudioFiles { get; set; }

        public DbSet<BlogTopic> BlogTopics { get; set; }
        public DbSet<BlogComment> BlogComments { get; set; }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<UserSubscription> UserSubscriptions { get; set; }
        public DbSet<SubscriptionInvoice> SubscriptionInvoices { get; set; }
        public DbSet<UserFeatureFlag> UserFeatureFlags { get; set; }
        public DbSet<UserGoogleToken> UserGoogleTokens { get; set; }
        public DbSet<UserWallet> UserWallets { get; set; }
        public DbSet<WalletTransaction> WalletTransactions { get; set; }
        public DbSet<PaymentOperation> PaymentOperations { get; set; }
        public DbSet<RecognitionUsage> RecognitionUsage { get; set; }
        public DbSet<TelegramAccountLink> TelegramAccountLinks { get; set; }
        public DbSet<TelegramLinkToken> TelegramLinkTokens { get; set; }

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

            builder.Entity<UserGoogleToken>(entity =>
            {
                entity.ToTable("UserGoogleTokens");
                entity.HasKey(t => t.UserId);
                entity.Property(t => t.TokenType)
                    .HasMaxLength(64)
                    .HasDefaultValue(GoogleTokenTypes.Calendar);
                entity.Property(t => t.Scope)
                    .HasMaxLength(1024);
                entity.Property(t => t.AccessToken)
                    .HasMaxLength(4096);
                entity.Property(t => t.RefreshToken)
                    .HasMaxLength(4096);
                entity.Property(t => t.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne(t => t.User)
                    .WithOne(u => u.GoogleToken)
                    .HasForeignKey<UserGoogleToken>(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ApplicationUser>()
                .HasOne(u => u.CurrentSubscription)
                .WithMany()
                .HasForeignKey(u => u.CurrentSubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ApplicationUser>()
                .Property(u => u.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            builder.Entity<TelegramAccountLink>(entity =>
            {
                entity.ToTable("TelegramAccountLinks");
                entity.HasKey(link => link.Id);
                entity.HasIndex(link => link.TelegramId).IsUnique();
                entity.Property(link => link.Status)
                    .HasConversion<int>();
                entity.Property(link => link.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne(link => link.User)
                    .WithMany(user => user.TelegramLinks)
                    .HasForeignKey(link => link.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TelegramLinkToken>(entity =>
            {
                entity.ToTable("TelegramLinkTokens");
                entity.HasKey(token => token.Id);
                entity.HasIndex(token => token.TokenHash).IsUnique();
                entity.Property(token => token.CreatedAt)
                    .HasDefaultValueSql("GETUTCDATE()");
                entity.Property(token => token.Purpose)
                    .HasMaxLength(64);
                entity.HasOne(token => token.Link)
                    .WithMany(link => link.Tokens)
                    .HasForeignKey(token => token.LinkId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

    }
}
