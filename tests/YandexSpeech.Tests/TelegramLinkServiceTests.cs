using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO.Telegram;
using YandexSpeech.services.Google;
using YandexSpeech.services.Options;
using YandexSpeech.services.TelegramIntegration;

namespace YandexSpeech.Tests;

public sealed class TelegramLinkServiceTests
{
    [Fact]
    public async Task CreateLinkTokenAsync_WithExistingLink_AddsNewToken()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        await using var dbContext = new MyDbContext(optionsBuilder.Options);

        var service = CreateService(dbContext);

        var context = new TelegramLinkInitiationContext(
            123456,
            "test_user",
            "Test",
            "User",
            "en");

        var firstResult = await service.CreateLinkTokenAsync(context, CancellationToken.None);
        Assert.NotNull(firstResult.Token);
        Assert.NotNull(firstResult.LinkUrl);

        var secondResult = await service.CreateLinkTokenAsync(context, CancellationToken.None);
        Assert.NotNull(secondResult.Token);
        Assert.NotEqual(firstResult.Token, secondResult.Token);

        var link = await dbContext.TelegramAccountLinks.Include(l => l.Tokens).SingleAsync();
        Assert.Equal(2, link.Tokens.Count);
    }

    [Fact]
    public async Task CreateLinkTokenAsync_WithExistingLinkWithoutTokens_CreatesToken()
    {
        var optionsBuilder = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        await using var dbContext = new MyDbContext(optionsBuilder.Options);

        var link = new TelegramAccountLink
        {
            Id = Guid.NewGuid(),
            TelegramId = 654321,
            Username = "another_user",
            FirstName = "Another",
            LastName = "User",
            LanguageCode = "en",
            CreatedAt = DateTime.UtcNow,
            Status = TelegramAccountLinkStatus.Pending
        };

        dbContext.TelegramAccountLinks.Add(link);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var context = new TelegramLinkInitiationContext(
            link.TelegramId,
            link.Username,
            link.FirstName,
            link.LastName,
            link.LanguageCode);

        var result = await service.CreateLinkTokenAsync(context, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));

        var tokens = await dbContext.TelegramLinkTokens
            .Where(t => t.LinkId == link.Id)
            .ToListAsync();

        Assert.Single(tokens);
    }

    private static TelegramLinkService CreateService(MyDbContext dbContext)
    {
        var optionsMonitor = new TestOptionsMonitor(new TelegramIntegrationOptions
        {
            LinkBaseUrl = "https://example.com/link",
            TokenSigningKey = "super-secret-signing-key",
            TokenLifetime = TimeSpan.FromMinutes(5),
            MaxActiveTokensPerLink = 5,
            StatusCacheDuration = TimeSpan.FromSeconds(1),
            LinkInactivityTimeout = TimeSpan.FromHours(1)
        });

        return new TelegramLinkService(
            dbContext,
            optionsMonitor,
            new NoopNotifier(),
            new StubGoogleTokenService(),
            NullLogger<TelegramLinkService>.Instance);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<TelegramIntegrationOptions>
    {
        private readonly TelegramIntegrationOptions _options;

        public TestOptionsMonitor(TelegramIntegrationOptions options)
        {
            _options = options;
        }

        public TelegramIntegrationOptions CurrentValue => _options;

        public TelegramIntegrationOptions Get(string? name) => _options;

        public IDisposable OnChange(Action<TelegramIntegrationOptions, string> listener) => NullDisposable.Instance;
    }

    private sealed class NoopNotifier : ITelegramIntegrationNotifier
    {
        public Task NotifyStatusAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyLinkCompletedAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyLinkFailedAsync(long telegramId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubGoogleTokenService : IGoogleTokenService
    {
        public Task<bool> HasCalendarAccessAsync(ApplicationUser user, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(ApplicationUser user, bool consentGranted, IEnumerable<AuthenticationToken>? tokens, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleTokenOperationResult(false, false, null, null, null));

        public Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleTokenOperationResult(false, false, null, null, null));

        public Task RecordCalendarDeclinedAsync(ApplicationUser user, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GoogleTokenOperationResult> RevokeAsync(ApplicationUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleTokenOperationResult(false, false, null, null, null));
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        private NullDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
