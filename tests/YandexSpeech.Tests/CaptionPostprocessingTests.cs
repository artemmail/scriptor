using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YoutubeExplode.Videos.ClosedCaptions;

namespace YandexSpeech.Tests;

public sealed class CaptionPostprocessingTests
{
    [Theory]
    [InlineData(20000)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void LongSingleCaptionIsSplitWithoutLosingText(int configuredSize)
    {
        var text = string.Join(" ", Enumerable.Range(0, 6000).Select(i => $"слово{i}"));
        var blocks = new CaptionProcessor().SegmentCaptionSegmentsDetailed(
            new List<CaptionSegment> { new() { Text = text, Time = 1, EndTime = 120 } }, configuredSize, 400);
        Assert.True(blocks.Count > 1);
        Assert.Equal(text, string.Join(" ", blocks.Select(b => b.Text)));
        Assert.All(blocks, b =>
        {
            Assert.InRange(b.Text.Length, 1, CaptionProcessor.MaxBlockCharacters);
            Assert.InRange(b.Text.Split(' ').Length, 1, CaptionProcessor.MaxBlockWords);
            Assert.Equal(0, b.StartIndex);
            Assert.Equal(0, b.EndIndex);
        });
    }

    [Fact]
    public void ShortCaptionsAndOversizedWordsAreNotLost()
    {
        var captions = new List<ClosedCaption>
        {
            new("Да", TimeSpan.Zero, TimeSpan.FromSeconds(1), Array.Empty<ClosedCaptionPart>()),
            new("Нет спасибо", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Array.Empty<ClosedCaptionPart>())
        };
        Assert.Equal("Да Нет спасибо", string.Join(" ", new CaptionProcessor().SegmentCaptions(captions)));
        var text = new string('я', 30000);
        var blocks = new CaptionProcessor().SegmentCaptionSegments(
            new List<CaptionSegment> { new() { Text = text } }, 20000);
        Assert.Equal(text, string.Concat(blocks));
        Assert.All(blocks, b => Assert.InRange(b.Length, 1, CaptionProcessor.MaxBlockCharacters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrEmptyPostprocessingDoesNotPublishRawText(bool empty)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
        var id = await SeedAsync(db, 3);
        var task = await db.YoutubeCaptionTasks.FindAsync(id);
        task!.Status = RecognizeStatus.ApplyingPunctuationSegment;
        task.Done = false;
        var segment = await db.RecognizedSegments.SingleAsync();
        segment.IsProcessed = false;
        segment.ProcessedText = null;
        await db.SaveChangesAsync();
        var service = new YoutubeCaptionService(db, new FailingPunctuation(empty), null!, null!,
            scope.ServiceProvider.GetRequiredService<ISubscriptionService>(), NullLogger<YoutubeCaptionService>.Instance);
        await service.ContinueCaptionTaskAsync(id);
        Assert.Equal(RecognizeStatus.Error, task.Status);
        Assert.False(task.Done);
        Assert.False(segment.IsProcessed);
        Assert.False(segment.IsProcessing);
        Assert.Null(segment.ProcessedText);
        Assert.Equal("previous result", task.Result);
        Assert.NotEmpty(task.Error!);
    }

    [Fact]
    public async Task ReprocessChargesOnceAndRebuildsSegmentsWhilePreservingResult()
    {
        using var provider = CreateProvider();
        string id;
        using (var scope = provider.CreateScope())
            id = await SeedAsync(scope.ServiceProvider.GetRequiredService<MyDbContext>(), 3);
        var manager = new CaptionTaskManager(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CaptionTaskManager>.Instance);
        var result = await manager.ReprocessCaptionTaskAsync(id, "owner", false);
        Assert.Equal(RecognizeStatus.SegmentingCaptions, result!.Status);
        Assert.False(result.Done);
        Assert.Equal("previous result", result.Result);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReprocessCaptionTaskAsync(id, "owner", false));
        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(2, (await db.UserSubscriptions.SingleAsync()).RemainingVideos);
        Assert.Empty(await db.RecognizedSegments.ToListAsync());
        Assert.Equal(0, (await db.YoutubeCaptionTasks.FindAsync(id))!.SegmentsProcessed);
    }

    [Theory]
    [InlineData("owner", 0)]
    [InlineData("stranger", 3)]
    public async Task ReprocessDeniedDoesNotChangeResultOrBalance(string requester, int balance)
    {
        using var provider = CreateProvider();
        string id;
        using (var scope = provider.CreateScope())
            id = await SeedAsync(scope.ServiceProvider.GetRequiredService<MyDbContext>(), balance);
        var manager = new CaptionTaskManager(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CaptionTaskManager>.Instance);
        if (requester == "owner")
            await Assert.ThrowsAsync<CaptionQuotaException>(() => manager.ReprocessCaptionTaskAsync(id, requester, false));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.ReprocessCaptionTaskAsync(id, requester, false));
        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<MyDbContext>();
        Assert.Equal(balance, (await db.UserSubscriptions.SingleAsync()).RemainingVideos);
        Assert.Equal(RecognizeStatus.Done, (await db.YoutubeCaptionTasks.FindAsync(id))!.Status);
        Assert.Equal("previous result", (await db.YoutubeCaptionTasks.FindAsync(id))!.Result);
        Assert.Single(await db.RecognizedSegments.ToListAsync());
    }

    private static ServiceProvider CreateProvider()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MyDbContext>(options => options.UseInMemoryDatabase(name));
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedAsync(MyDbContext db, int credits)
    {
        var id = Guid.NewGuid().ToString();
        db.YoutubeCaptionTasks.Add(new YoutubeCaptionTask
        {
            Id = id, UserId = "owner", Status = RecognizeStatus.Done, Done = true,
            Result = "previous result", SegmentsTotal = 1, SegmentsProcessed = 1,
            QuotaChargedAt = DateTime.UtcNow.AddDays(-1)
        });
        db.YoutubeCaptionTexts.Add(new YoutubeCaptionText { Id = id, Caption = "[]" });
        db.RecognizedSegments.Add(new RecognizedSegment
        {
            YoutubeCaptionTaskId = id, Text = "original", ProcessedText = "previous result", IsProcessed = true
        });
        db.UserSubscriptions.Add(new UserSubscription
        {
            Id = Guid.NewGuid(), UserId = "owner", Status = SubscriptionStatus.Active,
            GrantedVideos = credits, RemainingVideos = credits
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class FailingPunctuation(bool empty) : IPunctuationService
    {
        public Task<string> GetAvailableModelsAsync() => Task.FromResult("");
        public Task<string> FixPunctuationAsync(string rawText, string? previousContext, string profileName, string? clarification = null)
            => empty ? Task.FromResult(" ") : throw new InvalidOperationException("Simulated model failure");
    }
}
