using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YoutubeExplode.Videos.ClosedCaptions;

namespace YandexSpeech.Tests;

public sealed class CaptionTrackSelectionTests
{
    [Fact]
    public void TrackSelectionMigrationIsDiscoverable()
    {
        using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=True")
            .Options);

        Assert.Contains("20261003000000_CaptionTrackSelection", db.Database.GetMigrations());
        Assert.Contains("20261003000001_CaptionLanguageSlugs", db.Database.GetMigrations());
    }

    [Fact]
    public void CaptionTaskSlugsUseLanguageCodesAndRemainUnique()
    {
        using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var slugs = new YSubtitlesService(db);
        var existing = new List<string> { "sample-video" };
        var russian = new YoutubeCaptionTask
        {
            Id = "video-ru", VideoId = "video", Title = "Sample Video", CaptionTrackKey = "ru|auto|0"
        };
        var english = new YoutubeCaptionTask
        {
            Id = "video-en", VideoId = "video", Title = "Sample Video", CaptionTrackKey = "en-us|auto|0"
        };

        Assert.Equal("sample-video-ru", slugs.GenerateCaptionTaskSlug(russian, existing));
        Assert.Equal("sample-video-en", slugs.GenerateCaptionTaskSlug(english, existing));
        existing.Add("sample-video-ru");
        Assert.Equal("sample-video-ru-auto", slugs.GenerateCaptionTaskSlug(russian, existing));
        existing.Add("sample-video-ru-auto");
        Assert.Equal("sample-video-ru-video", slugs.GenerateCaptionTaskSlug(russian, existing));
    }

    [Fact]
    public async Task PreviousSlugStillResolvesToCanonicalTask()
    {
        var services = new ServiceCollection();
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<MyDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            db.YoutubeCaptionTasks.Add(new YoutubeCaptionTask
            {
                Id = "video", Slug = "sample-video-ru", PreviousSlug = "sample-video-1"
            });
            await db.SaveChangesAsync();
        }

        var manager = new CaptionTaskManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CaptionTaskManager>.Instance);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            Assert.NotNull(await db.YoutubeCaptionTasks.SingleOrDefaultAsync(
                item => item.PreviousSlug == "sample-video-1"));
        }
        var task = await manager.GetTaskStatusAsync("sample-video-1");
        Assert.Equal("sample-video-ru", task?.Slug);
    }

    [Fact]
    public void DistinguishesLanguageManualAndAutomaticTracks()
    {
        var tracks = new List<ClosedCaptionTrackInfo>
        {
            new("manual-1", new Language("ru", "Русский"), false),
            new("automatic", new Language("ru", "Русский"), true),
            new("manual-2", new Language("ru", "Русский"), false)
        };

        var options = CaptionTrackSelection.Describe(tracks);

        Assert.Equal(3, options.Select(option => option.Key).Distinct().Count());
        Assert.Same(tracks[1], CaptionTrackSelection.Find(tracks, options[1].Key));
        Assert.Same(tracks[2], CaptionTrackSelection.Find(tracks, options[2].Key));
        Assert.Null(CaptionTrackSelection.Find(tracks, "missing"));
    }

    [Fact]
    public async Task SameVideoCanHaveSeparateTasksForSelectedTracks()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<MyDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IYoutubeCaptionService, CompletingCaptionService>();
        using var provider = services.BuildServiceProvider();
        var manager = new CaptionTaskManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CaptionTaskManager>.Instance);

        const string videoId = "dQw4w9WgXcQ";
        var defaultId = await manager.EnqueueCaptionTaskAsync(videoId, "ip", "alice");
        var manualId = await manager.EnqueueCaptionTaskAsync(videoId, "ip", "alice", "ru|manual|0");
        var automaticId = await manager.EnqueueCaptionTaskAsync(videoId, "ip", "alice", "ru|auto|0");

        Assert.Equal(videoId, defaultId);
        Assert.NotEqual(defaultId, manualId);
        Assert.NotEqual(manualId, automaticId);
        Assert.Equal(manualId, await manager.EnqueueCaptionTaskAsync(videoId, "ip", "alice", "ru|manual|0"));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var tasks = await db.YoutubeCaptionTasks.AsNoTracking().ToListAsync();
        Assert.Equal(3, tasks.Count);
        Assert.All(tasks, task => Assert.Equal(videoId, task.VideoId));
        Assert.Equal("ru|manual|0", tasks.Single(task => task.Id == manualId).CaptionTrackKey);
    }

    private sealed class CompletingCaptionService(MyDbContext db) : IYoutubeCaptionService
    {
        public Task StartCaptionTaskAsync(string id, string createdBy) => Task.CompletedTask;
        public Task UpdateNullTitlesAsync() => Task.CompletedTask;

        public async Task ContinueCaptionTaskAsync(string id)
        {
            var task = await db.YoutubeCaptionTasks.FindAsync(id);
            if (task == null) return;
            task.Status = RecognizeStatus.Done;
            task.Done = true;
            task.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
