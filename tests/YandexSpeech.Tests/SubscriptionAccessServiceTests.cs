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
    public async Task UnlimitedVideoBalanceAllowsRecognition()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance
        {
            RemainingVideos = int.MaxValue,
            RemainingTranscriptionMinutes = int.MaxValue
        });

        var decision = await service.AuthorizeYoutubeRecognitionAsync("alice");

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.RemainingVideos);
        Assert.Null(decision.RemainingQuota);
    }

    [Fact]
    public async Task VideoAuthorizationReportsBalanceAfterRequestedVideo()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance { RemainingVideos = 2 });

        var decision = await service.AuthorizeYoutubeRecognitionAsync("alice");

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, decision.RemainingVideos);
        Assert.Equal(1, decision.RemainingQuota);
    }

    [Fact]
    public async Task ExhaustedVideoBalanceReturnsBillingLink()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance { RemainingVideos = 0 });

        var decision = await service.AuthorizeYoutubeRecognitionAsync("alice");

        Assert.False(decision.IsAllowed);
        Assert.Equal(0, decision.RemainingVideos);
        Assert.Equal("/billing", decision.PaymentUrl);
        Assert.Contains("Лимит видео", decision.Message);
    }

    [Fact]
    public async Task VideoRequestLargerThanBalanceIsDeniedWithoutChangingBalance()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance { RemainingVideos = 2 });

        var decision = await service.AuthorizeYoutubeRecognitionAsync("alice", requestedVideos: 3);

        Assert.False(decision.IsAllowed);
        Assert.Equal(2, decision.RemainingVideos);
        Assert.Contains("доступно 2", decision.Message);
    }

    [Fact]
    public async Task TranscriptionAuthorizationUsesMinuteBalanceWithoutConsumingIt()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance
        {
            RemainingVideos = 1,
            RemainingTranscriptionMinutes = 2
        });

        var first = await service.AuthorizeTranscriptionAsync("alice", 1);
        var repeated = await service.AuthorizeTranscriptionAsync("alice", 1);
        var tooLong = await service.AuthorizeTranscriptionAsync("alice", 3);

        Assert.True(first.IsAllowed);
        Assert.Equal(1, first.RemainingTranscriptionMinutes);
        Assert.Equal(1, repeated.RemainingTranscriptionMinutes);
        Assert.False(tooLong.IsAllowed);
        Assert.Equal(2, tooLong.MaxUploadMinutes);
        Assert.Equal("/billing", tooLong.PaymentUrl);
    }

    [Fact]
    public async Task AuthorizationRejectsUnknownUser()
    {
        await using var db = await CreateContextAsync("alice");
        var service = CreateService(db, new SubscriptionQuotaBalance { RemainingVideos = 5 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AuthorizeYoutubeRecognitionAsync("bob"));
    }

    private static SubscriptionAccessService CreateService(MyDbContext db, SubscriptionQuotaBalance balance)
        => new(
            db,
            new SubscriptionServiceStub { Balance = balance },
            Options.Create(new SubscriptionLimitsOptions { BillingRelativeUrl = "/billing" }),
            NullLogger<SubscriptionAccessService>.Instance);

    private static async Task<MyDbContext> CreateContextAsync(string userId)
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new MyDbContext(options);
        db.Users.Add(new ApplicationUser { Id = userId, Email = $"{userId}@example.com" });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class SubscriptionServiceStub : ISubscriptionService
    {
        public SubscriptionQuotaBalance Balance { get; init; } = new();

        public Task<SubscriptionQuotaBalance> GetQuotaBalanceAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Balance);

        public Task<UserSubscription?> GetActiveSubscriptionAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserSubscription?>(null);

        public Task<IReadOnlyList<UserSubscription>> GetActiveSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserSubscription>>(Array.Empty<UserSubscription>());

        public Task<UserSubscription> ActivateSubscriptionAsync(string userId, Guid planId, bool autoRenew = false,
            bool isLifetimeOverride = false, string? externalPaymentId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task CancelSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task RefreshUserCapabilitiesAsync(string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> TryConsumeQuotaAsync(string userId, int transcriptionMinutes, int videos,
            string? reference = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task EnsureWelcomePackageAsync(string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SubscriptionPlan> SavePlanAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
