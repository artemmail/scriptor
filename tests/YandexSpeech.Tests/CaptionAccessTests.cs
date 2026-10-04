using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;

namespace YandexSpeech.Tests;

public sealed class CaptionAccessTests
{
    [Fact]
    public async Task HiddenCaptionIsVisibleOnlyToOwnerAndAbsentFromPublicList()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        await SeedCaptionsAsync(db);
        var manager = CreateManager(provider);
        var subtitles = new YSubtitlesService(db);

        var guest = CreateController(manager, subtitles, db, null);
        Assert.IsType<NotFoundObjectResult>((await guest.GetStatus("hidden-slug")).Result);
        Assert.IsType<NotFoundObjectResult>((await guest.GetStatus("legacy-hidden")).Result);
        Assert.IsType<NotFoundObjectResult>((await guest.GetStatus("public-task")).Result);
        Assert.IsType<NotFoundObjectResult>(await guest.GenerateSrt("hidden-slug"));

        var owner = CreateController(manager, subtitles, db, "alice");
        Assert.IsType<OkObjectResult>((await owner.GetStatus("hidden-slug")).Result);
        var publicList = Assert.IsType<OkObjectResult>((await owner.GetAllTasks()).Result);
        Assert.Equal(1, ((System.Collections.IEnumerable)publicList.Value!).Cast<object>().Count());
        Assert.IsType<ForbidResult>(await owner.GetTasks(userId: "bob", includeHidden: true));
    }

    [Fact]
    public async Task CaptionMutationRequiresOwnerOrAdmin()
    {
        using var provider = CreateProvider();
        using (var scope = provider.CreateScope())
            await SeedCaptionsAsync(scope.ServiceProvider.GetRequiredService<MyDbContext>());
        var manager = CreateManager(provider);

        Assert.False(await manager.UpdateTaskResultAsync("hidden-slug", "tampered", "bob", false));
        Assert.False(await manager.DeleteTaskAsync("hidden-slug", "bob", false));
        Assert.Equal("secret", (await manager.GetTaskStatusAsync("hidden-slug"))?.Result);

        Assert.True(await manager.UpdateTaskResultAsync("hidden-slug", "edited", "alice", false));
        Assert.Equal("edited", (await manager.GetTaskStatusAsync("hidden-slug"))?.Result);
        Assert.True(await manager.DeleteTaskAsync("hidden-slug", "alice", false));
        Assert.Null(await manager.GetTaskStatusAsync("hidden-slug"));
        using var verifyScope = provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.False(await verifyDb.YoutubeCaptionTexts.AnyAsync(t => t.Id == "hidden-task"));
        Assert.False(await verifyDb.RecognizedSegments.AnyAsync(s => s.YoutubeCaptionTaskId == "hidden-task"));
    }

    private static ServiceProvider CreateProvider()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<MyDbContext>(options => options.UseInMemoryDatabase(name));
        return services.BuildServiceProvider();
    }

    private static CaptionTaskManager CreateManager(IServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CaptionTaskManager>.Instance);

    private static async Task SeedCaptionsAsync(MyDbContext db)
    {
        db.YoutubeCaptionTasks.AddRange(
            new YoutubeCaptionTask
            {
                Id = "public-task", Slug = "public-slug", UserId = "alice",
                Visibility = YoutubeCaptionVisibility.Public, Result = "public"
            },
            new YoutubeCaptionTask
            {
                Id = "hidden-task", Slug = "hidden-slug", UserId = "alice",
                Visibility = YoutubeCaptionVisibility.Hidden, Result = "secret"
            },
            new YoutubeCaptionTask
            {
                Id = "legacy-hidden", Visibility = YoutubeCaptionVisibility.Hidden,
                Result = "legacy secret"
            },
            new YoutubeCaptionTask
            {
                Id = "collision-hidden", Slug = "public-task", UserId = "alice",
                Visibility = YoutubeCaptionVisibility.Hidden, Result = "collision secret"
            });
        db.YoutubeCaptionTexts.Add(new YoutubeCaptionText { Id = "hidden-task", Caption = "[]" });
        db.RecognizedSegments.Add(new RecognizedSegment
        {
            YoutubeCaptionTaskId = "hidden-task", Order = 0, Text = "segment"
        });
        await db.SaveChangesAsync();
    }

    private static YSubtitilesController CreateController(
        CaptionTaskManager manager, YSubtitlesService subtitles, MyDbContext db, string? userId)
    {
        var claims = userId == null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.NameIdentifier, userId)];
        return new YSubtitilesController(
            manager, null!, null!, subtitles, null!, db,
            new SubscriptionService(db, NullLogger<SubscriptionService>.Instance), null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId == null ? null : "Test"))
                }
            }
        };
    }
}
