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
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;

namespace YandexSpeech.Tests;

public sealed class CaptionRetryBillingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpaidRetryChargesCallerAndKeepsDocumentOwner(bool hasSegments)
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, ownerCredits: 0, callerCredits: 3, hasSegments: hasSegments);
        var manager = Manager(provider);

        var result = await manager.RestartCaptionTaskAsync(id, "caller", isAdmin: true);

        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.False(result.Done);
        Assert.Equal(hasSegments ? RecognizeStatus.ApplyingPunctuationSegment : RecognizeStatus.Created, result.Status);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var task = await db.YoutubeCaptionTasks.SingleAsync();
        Assert.Equal("owner", task.UserId);
        Assert.NotNull(task.QuotaChargedAt);
        Assert.Equal(0, await Credits(db, "owner"));
        Assert.Equal(2, await Credits(db, "caller"));
        if (hasSegments) Assert.False((await db.RecognizedSegments.SingleAsync()).IsProcessing);
    }

    [Fact]
    public async Task CallerWithoutCreditsDoesNotSpendOwnersBalanceOrClearFailure()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, ownerCredits: 5, callerCredits: 0, hasSegments: true);
        await Assert.ThrowsAsync<CaptionQuotaException>(() => Manager(provider).RestartCaptionTaskAsync(id, "caller", true));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var task = await db.YoutubeCaptionTasks.SingleAsync();
        Assert.Equal(RecognizeStatus.Error, task.Status);
        Assert.True(task.Done);
        Assert.Equal("original quota error", task.Error);
        Assert.Null(task.QuotaChargedAt);
        Assert.True((await db.RecognizedSegments.SingleAsync()).IsProcessing);
        Assert.Equal(5, await Credits(db, "owner"));
        Assert.Equal(0, await Credits(db, "caller"));
    }

    [Fact]
    public async Task PaidAttemptResumesWithoutAnotherCharge()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, 2, 3, charged: true);
        await Manager(provider).RestartCaptionTaskAsync(id, "caller", true);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(2, await Credits(db, "owner"));
        Assert.Equal(3, await Credits(db, "caller"));
        Assert.Equal(RecognizeStatus.Created, (await db.YoutubeCaptionTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task RepeatedRetryChargesOnlyOnce()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, 0, 3);
        var manager = Manager(provider);
        await Task.WhenAll(manager.RestartCaptionTaskAsync(id, "caller", true), manager.RestartCaptionTaskAsync(id, "caller", true));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(2, await Credits(db, "caller"));
        Assert.Equal(0, await Credits(db, "owner"));
    }

    [Fact]
    public async Task UnauthorizedRetryCannotChargeEitherAccount()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, 2, 3);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Manager(provider).RestartCaptionTaskAsync(id, "caller", false));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(2, await Credits(db, "owner"));
        Assert.Equal(3, await Credits(db, "caller"));
        Assert.Equal(RecognizeStatus.Error, (await db.YoutubeCaptionTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task RestartEndpointReturnsCallersQuotaErrorImmediately()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, 4, 0);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var controller = new YSubtitilesController(Manager(provider), null!, null!, null!, null!, db,
            scope.ServiceProvider.GetRequiredService<ISubscriptionService>(), null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "caller"), new Claim(ClaimTypes.Role, "Admin")
                    }, "Test"))
                }
            }
        };
        var response = Assert.IsType<ObjectResult>((await controller.RestartTask(id)).Result);
        Assert.Equal(402, response.StatusCode);
        Assert.Contains("на вашем счёте", System.Text.Json.JsonSerializer.Serialize(response.Value,
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public async Task FullReprocessingAlsoChargesCallerRatherThanOriginalOwner()
    {
        using var provider = CreateProvider();
        var id = await SeedAsync(provider, 0, 3, charged: true);
        await Manager(provider).ReprocessCaptionTaskAsync(id, "caller", true);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(0, await Credits(db, "owner"));
        Assert.Equal(2, await Credits(db, "caller"));
        Assert.Equal("owner", (await db.YoutubeCaptionTasks.SingleAsync()).UserId);
    }

    private static ServiceProvider CreateProvider()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MyDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services.BuildServiceProvider();
    }

    private static CaptionTaskManager Manager(IServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CaptionTaskManager>.Instance);

    private static Task<int> Credits(MyDbContext db, string userId) => db.UserSubscriptions
        .Where(s => s.UserId == userId).Select(s => s.RemainingVideos).SingleAsync();

    private static async Task<string> SeedAsync(IServiceProvider provider, int ownerCredits, int callerCredits,
        bool hasSegments = false, bool charged = false)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var id = Guid.NewGuid().ToString();
        db.YoutubeCaptionTasks.Add(new YoutubeCaptionTask
        {
            Id = id, UserId = "owner", Status = RecognizeStatus.Error, Done = true,
            Error = "original quota error", QuotaChargedAt = charged ? DateTime.UtcNow.AddHours(-1) : null
        });
        foreach (var (userId, credits) in new[] { ("owner", ownerCredits), ("caller", callerCredits) })
            db.UserSubscriptions.Add(new UserSubscription
            {
                Id = Guid.NewGuid(), UserId = userId, Status = SubscriptionStatus.Active,
                GrantedVideos = credits, RemainingVideos = credits
            });
        if (hasSegments)
            db.RecognizedSegments.Add(new RecognizedSegment { YoutubeCaptionTaskId = id, Text = "source", IsProcessing = true });
        await db.SaveChangesAsync();
        return id;
    }
}
