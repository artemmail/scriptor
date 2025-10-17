using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Models;
using YandexSpeech.services.Options;

namespace YandexSpeech.Tests;

public sealed class SubscriptionAccessServiceTests
{
    [Fact]
    public async Task AuthorizeYoutubeRecognitionAsync_AllowsForLifetimeAccess()
    {
        await using var dbContext = CreateContext(out _);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            Email = "user1@example.com",
            HasLifetimeAccess = true
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, new SubscriptionServiceStub());

        var decision = await service.AuthorizeYoutubeRecognitionAsync("user-1");

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.RecognizedTitles);
    }

    [Fact]
    public async Task AuthorizeYoutubeRecognitionAsync_AllowsWhenUnderLimit()
    {
        await using var dbContext = CreateContext(out _);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-under-limit",
            Email = "user2@example.com",
            HasLifetimeAccess = false
        });
        await dbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        dbContext.YoutubeCaptionTasks.Add(new YoutubeCaptionTask
        {
            Id = "video-1",
            UserId = "user-under-limit",
            Title = "First",
            Done = true,
            Status = RecognizeStatus.Done,
            CreatedAt = now.AddHours(-2),
            ModifiedAt = now.AddHours(-1)
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SubscriptionLimitsOptions
        {
            FreeYoutubeRecognitionsPerDay = 3,
            BillingRelativeUrl = "/billing"
        });

        var service = CreateService(dbContext, new SubscriptionServiceStub(), options);

        var decision = await service.AuthorizeYoutubeRecognitionAsync("user-under-limit");

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.RemainingQuota);
        Assert.Null(decision.RecognizedTitles);
    }

    [Fact]
    public async Task AuthorizeYoutubeRecognitionAsync_DeniesWhenDailyLimitReached()
    {
        await using var dbContext = CreateContext(out _);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-2",
            Email = "user2@example.com",
            HasLifetimeAccess = false
        });

        var now = DateTime.UtcNow;

        dbContext.YoutubeCaptionTasks.AddRange(
            new YoutubeCaptionTask
            {
                Id = "included-1",
                UserId = "user-2",
                Title = "Video C",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddHours(-3),
                ModifiedAt = now.AddHours(-2)
            },
            new YoutubeCaptionTask
            {
                Id = "included-2",
                UserId = "user-2",
                Title = "Video B",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddHours(-2),
                ModifiedAt = now.AddHours(-1)
            },
            new YoutubeCaptionTask
            {
                Id = "included-3",
                UserId = "user-2",
                Title = "Video A",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddMinutes(-30),
                ModifiedAt = now.AddMinutes(-15)
            },
            new YoutubeCaptionTask
            {
                Id = "excluded-old",
                UserId = "user-2",
                Title = "Old",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddDays(-2),
                ModifiedAt = now.AddDays(-2)
            },
            new YoutubeCaptionTask
            {
                Id = "excluded-error",
                UserId = "user-2",
                Title = "Error",
                Done = true,
                Status = RecognizeStatus.Error,
                CreatedAt = now.AddHours(-1),
                ModifiedAt = now.AddMinutes(-40)
            },
            new YoutubeCaptionTask
            {
                Id = "excluded-incomplete",
                UserId = "user-2",
                Title = "Processing",
                Done = false,
                Status = RecognizeStatus.InProgress,
                CreatedAt = now.AddMinutes(-10)
            });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SubscriptionLimitsOptions
        {
            FreeYoutubeRecognitionsPerDay = 3,
            BillingRelativeUrl = "/billing"
        });

        var service = CreateService(dbContext, new SubscriptionServiceStub(), options);

        var decision = await service.AuthorizeYoutubeRecognitionAsync("user-2");

        Assert.False(decision.IsAllowed);
        Assert.NotNull(decision.Message);
        Assert.Equal("/billing", decision.PaymentUrl);
        Assert.Equal(0, decision.RemainingQuota);
        Assert.NotNull(decision.RecognizedTitles);
        Assert.Equal(new[] { "Video A", "Video B", "Video C" }, decision.RecognizedTitles);
        Assert.Contains("Уже распознаны", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeTranscriptionAsync_RespectsMonthlyFreeLimit()
    {
        await using var dbContext = CreateContext(out _);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-3",
            Email = "user3@example.com",
            HasLifetimeAccess = false
        });
        await dbContext.SaveChangesAsync();

        var options = Options.Create(new SubscriptionLimitsOptions
        {
            FreeTranscriptionsPerMonth = 2,
            BillingRelativeUrl = "/billing"
        });

        var service = CreateService(dbContext, new SubscriptionServiceStub(), options);

        var first = await service.AuthorizeTranscriptionAsync("user-3");
        Assert.True(first.IsAllowed);
        Assert.Equal(1, first.RemainingQuota);

        var second = await service.AuthorizeTranscriptionAsync("user-3");
        Assert.True(second.IsAllowed);
        Assert.Equal(0, second.RemainingQuota);

        var third = await service.AuthorizeTranscriptionAsync("user-3");
        Assert.False(third.IsAllowed);
        Assert.Equal(0, third.RemainingQuota);
        Assert.NotNull(third.Message);

        var flags = await dbContext.UserFeatureFlags.ToListAsync();
        Assert.Single(flags);
        Assert.Equal("usage:transcriptions:" + DateTime.UtcNow.ToString("yyyy-MM"), flags[0].FeatureCode);
        Assert.Equal("2", flags[0].Value);
    }

    private static SubscriptionAccessService CreateService(
        MyDbContext dbContext,
        SubscriptionServiceStub subscription,
        IOptions<SubscriptionLimitsOptions>? options = null)
    {
        return new SubscriptionAccessService(
            dbContext,
            subscription,
            options ?? Options.Create(new SubscriptionLimitsOptions()),
            NullLogger<SubscriptionAccessService>.Instance);
    }

    private static MyDbContext CreateContext(out DbContextOptions<MyDbContext> options)
    {
        options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private sealed class SubscriptionServiceStub : ISubscriptionService
    {
        public UserSubscription? ActiveSubscription { get; set; }

        public Task<UserSubscription?> GetActiveSubscriptionAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ActiveSubscription);
        }

        public Task<UserSubscription> ActivateSubscriptionAsync(string userId, Guid planId, bool autoRenew = false, bool isLifetimeOverride = false, string? externalPaymentId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task CancelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task RefreshUserCapabilitiesAsync(string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SubscriptionPlan> SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

}
