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

        var usageStub = new UsageServiceStub
        {
            RegisterAsync = (_, _, _, _, _, _) =>
            {
                return Task.FromResult(new RecognitionUsage
                {
                    Id = Guid.NewGuid(),
                    UserId = "user-1",
                    RecognitionsCount = 1,
                    Currency = "RUB"
                });
            }
        };

        var service = CreateService(dbContext, usageStub, new SubscriptionServiceStub());

        var decision = await service.AuthorizeYoutubeRecognitionAsync("user-1");

        Assert.True(decision.IsAllowed);
        Assert.Single(usageStub.RegisterCalls);
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
        await dbContext.SaveChangesAsync();

        var usageStub = new UsageServiceStub
        {
            EvaluateAsync = (_, _, _, _, _) =>
            {
                return Task.FromResult(new UsageEvaluationResult(false, 0));
            }
        };

        var options = Options.Create(new SubscriptionLimitsOptions
        {
            FreeYoutubeRecognitionsPerDay = 3,
            BillingRelativeUrl = "/billing"
        });

        var service = new SubscriptionAccessService(
            dbContext,
            new SubscriptionServiceStub(),
            usageStub,
            options,
            NullLogger<SubscriptionAccessService>.Instance);

        var decision = await service.AuthorizeYoutubeRecognitionAsync("user-2");

        Assert.False(decision.IsAllowed);
        Assert.NotNull(decision.Message);
        Assert.Equal("/billing", decision.PaymentUrl);
        Assert.Equal(0, decision.RemainingQuota);
        Assert.Empty(usageStub.RegisterCalls);
        Assert.Single(usageStub.EvaluationCalls);
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

        var service = new SubscriptionAccessService(
            dbContext,
            new SubscriptionServiceStub(),
            new UsageServiceStub(),
            options,
            NullLogger<SubscriptionAccessService>.Instance);

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
        UsageServiceStub usage,
        SubscriptionServiceStub subscription,
        IOptions<SubscriptionLimitsOptions>? options = null)
    {
        return new SubscriptionAccessService(
            dbContext,
            subscription,
            usage,
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

    private sealed class UsageServiceStub : IUsageService
    {
        public List<(string UserId, int Requested, int? DailyLimit)> EvaluationCalls { get; } = new();
        public List<(string UserId, int Recognitions)> RegisterCalls { get; } = new();

        public Func<string, int, int?, bool, CancellationToken, Task<UsageEvaluationResult>> EvaluateAsync { get; set; }
            = (userId, requested, limit, _, _) =>
            {
                return Task.FromResult(new UsageEvaluationResult(true, Math.Max(limit ?? 0, 0) - requested));
            };

        public Func<string, int, decimal, string, Guid?, CancellationToken, Task<RecognitionUsage>> RegisterAsync { get; set; }
            = (userId, recognitions, _, currency, _, _) =>
            {
                return Task.FromResult(new RecognitionUsage
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    RecognitionsCount = recognitions,
                    Currency = currency
                });
            };

        public Task<UsageEvaluationResult> EvaluateDailyQuotaAsync(string userId, int requestedRecognitions, int? dailyLimit, bool hasUnlimitedQuota, CancellationToken cancellationToken = default)
        {
            EvaluationCalls.Add((userId, requestedRecognitions, dailyLimit));
            return EvaluateAsync(userId, requestedRecognitions, dailyLimit, hasUnlimitedQuota, cancellationToken);
        }

        public Task<RecognitionUsage> RegisterUsageAsync(string userId, int recognitionsCount, decimal chargeAmount, string currency, Guid? walletTransactionId = null, CancellationToken cancellationToken = default)
        {
            RegisterCalls.Add((userId, recognitionsCount));
            return RegisterAsync(userId, recognitionsCount, chargeAmount, currency, walletTransactionId, cancellationToken);
        }
    }
}
