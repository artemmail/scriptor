using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Requests;
using Google.Apis.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO.Telegram;
using YandexSpeech.services.Google;
using YandexSpeech.services.Options;

namespace YandexSpeech.services.TelegramIntegration
{
    public sealed class TelegramLinkService : ITelegramLinkService
    {
        private readonly MyDbContext _dbContext;
        private readonly IOptionsMonitor<TelegramIntegrationOptions> _optionsMonitor;
        private readonly ITelegramIntegrationNotifier _notifier;
        private readonly IGoogleTokenService _googleTokenService;
        private readonly ILogger<TelegramLinkService> _logger;

        public TelegramLinkService(
            MyDbContext dbContext,
            IOptionsMonitor<TelegramIntegrationOptions> optionsMonitor,
            ITelegramIntegrationNotifier notifier,
            IGoogleTokenService googleTokenService,
            ILogger<TelegramLinkService> logger)
        {
            _dbContext = dbContext;
            _optionsMonitor = optionsMonitor;
            _notifier = notifier;
            _googleTokenService = googleTokenService;
            _logger = logger;
        }

        public async Task<TelegramLinkInitiationResult> CreateLinkTokenAsync(
            TelegramLinkInitiationContext context,
            CancellationToken cancellationToken = default)
        {
            var options = _optionsMonitor.CurrentValue;
            options.Validate();

            const int maxRetryCount = 3;

            for (var attempt = 0; attempt < maxRetryCount; attempt++)
            {
                await using var transaction = await _dbContext.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    var now = DateTime.UtcNow;
                    var link = await _dbContext.TelegramAccountLinks
                        .FirstOrDefaultAsync(l => l.TelegramId == context.TelegramId, cancellationToken)
                        .ConfigureAwait(false);

                    List<TelegramLinkToken> tokens;

                    if (link == null)
                    {
                        link = new TelegramAccountLink
                        {
                            Id = Guid.NewGuid(),
                            TelegramId = context.TelegramId,
                            Username = Truncate(context.Username, 255),
                            FirstName = Truncate(context.FirstName, 255),
                            LastName = Truncate(context.LastName, 255),
                            LanguageCode = Truncate(context.LanguageCode, 10),
                            CreatedAt = now,
                            Status = TelegramAccountLinkStatus.Pending
                        };

                        tokens = new List<TelegramLinkToken>();
                        link.Tokens = tokens;

                        _dbContext.TelegramAccountLinks.Add(link);
                    }
                    else
                    {
                        tokens = await _dbContext.TelegramLinkTokens
                            .Where(t => t.LinkId == link.Id)
                            .AsTracking()
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        link.Tokens = tokens;

                        if (link.UserId == null && link.Status != TelegramAccountLinkStatus.Pending)
                        {
                            link.Status = TelegramAccountLinkStatus.Pending;
                            link.RevokedAt = null;
                        }

                        if (HasProfileUpdates(link, context))
                        {
                            link.Username = Truncate(context.Username, 255);
                            link.FirstName = Truncate(context.FirstName, 255);
                            link.LastName = Truncate(context.LastName, 255);
                            link.LanguageCode = Truncate(context.LanguageCode, 10);
                        }

                        link.LastActivityAt = now;
                    }

                    CleanExpiredTokens(tokens, now);

                    if (options.MaxActiveTokensPerLink > 0
                        && tokens.Count(t => t.RevokedAt == null && t.ConsumedAt == null && t.ExpiresAt > now) >= options.MaxActiveTokensPerLink)
                    {
                        var oldest = tokens
                            .Where(t => t.RevokedAt == null && t.ConsumedAt == null && t.ExpiresAt > now)
                            .OrderBy(t => t.CreatedAt)
                            .FirstOrDefault();
                        if (oldest != null)
                        {
                            oldest.RevokedAt = now;
                        }
                    }

                    var token = GenerateToken();
                    var hash = HashToken(token, options.TokenSigningKey);

                    var tokenEntity = new TelegramLinkToken
                    {
                        Id = Guid.NewGuid(),
                        Link = link,
                        LinkId = link.Id,
                        TokenHash = hash,
                        CreatedAt = now,
                        ExpiresAt = now + options.TokenLifetime,
                        Purpose = TelegramLinkTokenPurposes.Link,
                        IsOneTime = true
                    };

                    _dbContext.TelegramLinkTokens.Add(tokenEntity);
                    tokens.Add(tokenEntity);
                    link.LastActivityAt = now;

                    await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    var linkUrl = BuildLinkUrl(options, token);
                    var status = await MapStatusAsync(link, cancellationToken).ConfigureAwait(false);

                    return new TelegramLinkInitiationResult
                    {
                        Token = token,
                        LinkUrl = linkUrl,
                        ExpiresAt = tokenEntity.ExpiresAt,
                        Status = status
                    };
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await RollbackSilentlyAsync(transaction, cancellationToken).ConfigureAwait(false);

                    await LogConcurrencyConflictAsync(ex, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested || attempt == maxRetryCount - 1)
                    {
                        throw;
                    }

                    _logger.LogWarning(ex, "Concurrency conflict while creating Telegram link token for TelegramId {TelegramId}. Retrying (attempt {Attempt}/{MaxAttempts}).", context.TelegramId, attempt + 1, maxRetryCount);
                    _dbContext.ChangeTracker.Clear();
                    var backoff = TimeSpan.FromMilliseconds(50 * (attempt + 1));
                    if (backoff > TimeSpan.Zero)
                    {
                        try
                        {
                            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    continue;
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    await RollbackSilentlyAsync(transaction, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested || attempt == maxRetryCount - 1)
                    {
                        throw new DbUpdateConcurrencyException("A unique constraint violation occurred while creating a telegram link token.", ex);
                    }

                    _logger.LogWarning(ex, "Unique constraint violation while creating Telegram link token for TelegramId {TelegramId}. Retrying (attempt {Attempt}/{MaxAttempts}).", context.TelegramId, attempt + 1, maxRetryCount);
                    _dbContext.ChangeTracker.Clear();
                    continue;
                }
            }

            throw new InvalidOperationException($"Unable to create Telegram link token for TelegramId {context.TelegramId} due to repeated concurrency conflicts.");
        }

        public async Task<TelegramLinkConfirmationResult> ConfirmLinkAsync(
            string token,
            string userId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new TelegramLinkConfirmationResult
                {
                    Success = false,
                    Error = "token_missing",
                    State = TelegramIntegrationStates.Error
                };
            }

            var options = _optionsMonitor.CurrentValue;
            options.Validate();

            var hash = HashToken(token, options.TokenSigningKey);

            var tokenEntity = await _dbContext.TelegramLinkTokens
                .Include(t => t.Link)
                    .ThenInclude(l => l.User)
                .Include(t => t.Link)
                    .ThenInclude(l => l.Tokens)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
                .ConfigureAwait(false);

            if (tokenEntity == null)
            {
                return new TelegramLinkConfirmationResult
                {
                    Success = false,
                    Error = "token_not_found",
                    State = TelegramIntegrationStates.Error
                };
            }

            var now = DateTime.UtcNow;

            if (tokenEntity.RevokedAt.HasValue)
            {
                return new TelegramLinkConfirmationResult
                {
                    Success = false,
                    Error = "token_revoked",
                    State = TelegramIntegrationStates.Error
                };
            }

            if (tokenEntity.ConsumedAt.HasValue)
            {
                return new TelegramLinkConfirmationResult
                {
                    Success = false,
                    Error = "token_consumed",
                    State = TelegramIntegrationStates.Error
                };
            }

            if (tokenEntity.ExpiresAt <= now)
            {
                tokenEntity.RevokedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return new TelegramLinkConfirmationResult
                {
                    Success = false,
                    Error = "token_expired",
                    State = TelegramIntegrationStates.Error
                };
            }

            var link = tokenEntity.Link;
            link.UserId = userId;
            link.Status = TelegramAccountLinkStatus.Linked;
            link.LinkedAt = now;
            link.LastActivityAt = now;
            link.LastStatusCheckAt = now;
            link.RevokedAt = null;

            tokenEntity.ConsumedAt = now;

            // Отзываем остальные активные токены
            foreach (var other in link.Tokens.Where(t => t.Id != tokenEntity.Id && t.ConsumedAt == null && t.RevokedAt == null))
            {
                other.RevokedAt = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var status = await MapStatusAsync(link, cancellationToken).ConfigureAwait(false);

            await _notifier.NotifyLinkCompletedAsync(link.TelegramId, status, cancellationToken).ConfigureAwait(false);

            return new TelegramLinkConfirmationResult
            {
                Success = true,
                State = TelegramIntegrationStates.Linked,
                Status = status
            };
        }

        public async Task<TelegramCalendarStatusDto> GetCalendarStatusAsync(long telegramId, CancellationToken cancellationToken = default)
        {
            var link = await _dbContext.TelegramAccountLinks
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.TelegramId == telegramId, cancellationToken)
                .ConfigureAwait(false);

            if (link == null)
            {
                return TelegramCalendarStatusDto.NotLinked();
            }

            link.LastStatusCheckAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return await MapStatusAsync(link, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TelegramCalendarStatusDto> RefreshCalendarStatusAsync(long telegramId, CancellationToken cancellationToken = default)
        {
            var link = await _dbContext.TelegramAccountLinks
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.TelegramId == telegramId, cancellationToken)
                .ConfigureAwait(false);

            if (link == null)
            {
                return TelegramCalendarStatusDto.NotLinked();
            }

            if (link.User != null)
            {
                try
                {
                    var ensureResult = await _googleTokenService.EnsureAccessTokenAsync(link.User, cancellationToken).ConfigureAwait(false);
                    if (!ensureResult.Succeeded)
                    {
                        _logger.LogInformation("Failed to refresh Google token for user {UserId}: {Error}", link.User.Id, ensureResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Failed to refresh Google Calendar tokens for user {UserId}", link.User.Id);
                    }
                }
            }

            link.LastStatusCheckAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return await MapStatusAsync(link, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TelegramCalendarEventResult> CreateTestEventAsync(long telegramId, CancellationToken cancellationToken = default)
        {
            var link = await _dbContext.TelegramAccountLinks
                .Include(l => l.User)
                .ThenInclude(u => u.GoogleToken)
                .FirstOrDefaultAsync(l => l.TelegramId == telegramId, cancellationToken)
                .ConfigureAwait(false);

            if (link == null || link.Status != TelegramAccountLinkStatus.Linked || string.IsNullOrWhiteSpace(link.UserId) || link.User == null)
            {
                return TelegramCalendarEventResult.Failure("Telegram account is not linked.");
            }

            if (link.User.GoogleToken == null || link.User.GoogleToken.RevokedAt.HasValue)
            {
                return TelegramCalendarEventResult.Failure("Google Calendar access is not available.");
            }

            var tokenResult = await _googleTokenService.EnsureAccessTokenAsync(link.User, cancellationToken).ConfigureAwait(false);
            if (!tokenResult.Succeeded || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
            {
                var error = tokenResult.ErrorMessage ?? "Unable to obtain Google access token.";
                return TelegramCalendarEventResult.Failure(error);
            }

            try
            {
                var credential = GoogleCredential.FromAccessToken(tokenResult.AccessToken);
                using var calendarService = new CalendarService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Scriptor Telegram Integration"
                });

                CalendarListEntry calendar;
                try
                {
                    calendar = await calendarService.CalendarList.Get("primary").ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (GoogleApiException ex)
                {
                    _logger.LogError(ex, "Failed to fetch calendar metadata for TelegramId {TelegramId}.", telegramId);
                    return TelegramCalendarEventResult.Failure(ex.Message);
                }

                var googleTimeZone = string.IsNullOrWhiteSpace(calendar.TimeZone) ? "UTC" : calendar.TimeZone;
                var calculationTimeZoneId = googleTimeZone;

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        calculationTimeZoneId = TimeZoneInfo.ConvertIanaIdToWindowsId(calculationTimeZoneId);
                    }
                    catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                    {
                        _logger.LogWarning(ex, "Failed to convert IANA timezone {TimeZone} to Windows id. Falling back to UTC.", googleTimeZone);
                        calculationTimeZoneId = "UTC";
                        googleTimeZone = "UTC";
                    }
                }

                TimeZoneInfo timeZone;
                try
                {
                    timeZone = TimeZoneInfo.FindSystemTimeZoneById(calculationTimeZoneId);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    _logger.LogWarning(ex, "Time zone {TimeZone} is not available on this system. Falling back to UTC.", calculationTimeZoneId);
                    googleTimeZone = "UTC";
                    timeZone = TimeZoneInfo.Utc;
                }

                var nowInZone = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
                var daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowInZone.DayOfWeek + 7) % 7;
                if (daysUntilMonday == 0)
                {
                    daysUntilMonday = 7;
                }

                var eventStartLocal = nowInZone.Date.AddDays(daysUntilMonday).AddHours(9);
                var eventEndLocal = eventStartLocal.AddHours(1);

                var startOffset = new DateTimeOffset(eventStartLocal, timeZone.GetUtcOffset(eventStartLocal));
                var endOffset = new DateTimeOffset(eventEndLocal, timeZone.GetUtcOffset(eventEndLocal));

                var calendarEvent = new Event
                {
                    Summary = "Scriptor Telegram test event",
                    Description = "Создано командой /testevent в Telegram боте.",
                    Start = new EventDateTime
                    {
                        DateTime = eventStartLocal,
                        TimeZone = googleTimeZone
                    },
                    End = new EventDateTime
                    {
                        DateTime = eventEndLocal,
                        TimeZone = googleTimeZone
                    }
                };

                Event createdEvent;
                try
                {
                    createdEvent = await calendarService.Events.Insert(calendarEvent, "primary").ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (GoogleApiException ex)
                {
                    _logger.LogError(ex, "Google API error while creating test event for TelegramId {TelegramId}.", telegramId);
                    return TelegramCalendarEventResult.Failure(ex.Message);
                }

                link.LastActivityAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return new TelegramCalendarEventResult
                {
                    Success = true,
                    EventId = createdEvent.Id,
                    HtmlLink = createdEvent.HtmlLink,
                    StartsAt = startOffset,
                    EndsAt = endOffset,
                    TimeZone = googleTimeZone
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating test event for TelegramId {TelegramId}.", telegramId);
                return TelegramCalendarEventResult.Failure(ex.Message);
            }
        }

        public async Task<bool> UnlinkAsync(long telegramId, string? initiatedByUserId, CancellationToken cancellationToken = default)
        {
            var link = await _dbContext.TelegramAccountLinks
                .FirstOrDefaultAsync(l => l.TelegramId == telegramId, cancellationToken)
                .ConfigureAwait(false);

            if (link == null)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            link.Status = TelegramAccountLinkStatus.Revoked;
            link.RevokedAt = now;
            link.LastActivityAt = now;
            link.UserId = initiatedByUserId == link.UserId ? null : link.UserId;

            foreach (var token in await _dbContext.TelegramLinkTokens.Where(t => t.LinkId == link.Id && t.RevokedAt == null && t.ConsumedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                token.RevokedAt = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var status = await MapStatusAsync(link, cancellationToken).ConfigureAwait(false);
            await _notifier.NotifyStatusAsync(link.TelegramId, status, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public Task<TelegramAccountLink?> FindLinkByUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            return _dbContext.TelegramAccountLinks
                .AsNoTracking()
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        }

        private static bool HasProfileUpdates(TelegramAccountLink link, TelegramLinkInitiationContext context)
        {
            return !string.Equals(link.Username, context.Username, StringComparison.Ordinal)
                   || !string.Equals(link.FirstName, context.FirstName, StringComparison.Ordinal)
                   || !string.Equals(link.LastName, context.LastName, StringComparison.Ordinal)
                   || !string.Equals(link.LanguageCode, context.LanguageCode, StringComparison.Ordinal);
        }

        private async Task LogConcurrencyConflictAsync(DbUpdateConcurrencyException exception, CancellationToken cancellationToken)
        {
            foreach (var entry in exception.Entries)
            {
                var entityType = entry.Metadata.ClrType.Name;
                var keyValues = entry.Properties
                    .Where(property => property.Metadata.IsPrimaryKey())
                    .ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);

                PropertyValues? databaseValues;

                try
                {
                    databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception logException)
                {
                    _logger.LogWarning(
                        logException,
                        "Concurrency conflict detected for entity {EntityType} with key {KeyValues}, but database values could not be retrieved.",
                        entityType,
                        keyValues);
                    continue;
                }

                if (databaseValues == null)
                {
                    _logger.LogWarning("Concurrency conflict detected for entity {EntityType} with key {KeyValues}: the record was deleted from the database.", entityType, keyValues);
                    continue;
                }

                var databaseSnapshot = databaseValues.Properties
                    .ToDictionary(property => property.Name, property => databaseValues[property]);

                _logger.LogWarning("Concurrency conflict detected for entity {EntityType} with key {KeyValues}. Database snapshot: {@DatabaseValues}.", entityType, keyValues, databaseSnapshot);
            }
        }

        private static void CleanExpiredTokens(ICollection<TelegramLinkToken> tokens, DateTime now)
        {
            foreach (var token in tokens
                         .Where(t => t.RevokedAt == null && t.ConsumedAt == null && t.ExpiresAt <= now)
                         .ToList())
            {
                token.RevokedAt = now;
            }
        }

        private static async Task RollbackSilentlyAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignored - rollback best effort only
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        {
            if (exception.InnerException is SqlException sqlException)
            {
                return sqlException.Number is 2601 or 2627;
            }

            return false;
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .TrimEnd('=');
        }

        private static string HashToken(string token, string signingKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hash);
        }

        private Uri BuildLinkUrl(TelegramIntegrationOptions options, string token)
        {
            if (!Uri.TryCreate(options.LinkBaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("Telegram link base URL is invalid.");
            }

            var builder = new UriBuilder(baseUri)
            {
                Query = string.IsNullOrEmpty(baseUri.Query)
                    ? $"token={Uri.EscapeDataString(token)}"
                    : baseUri.Query.TrimStart('?') + "&token=" + Uri.EscapeDataString(token)
            };

            return builder.Uri;
        }

        private async Task<TelegramCalendarStatusDto> MapStatusAsync(TelegramAccountLink link, CancellationToken cancellationToken)
        {
            if (link.UserId == null)
            {
                return new TelegramCalendarStatusDto
                {
                    Linked = false,
                    GoogleAuthorized = false,
                    AccessTokenExpired = false,
                    HasRequiredScope = false,
                    PermissionScope = null,
                    State = link.Status == TelegramAccountLinkStatus.Revoked
                        ? TelegramIntegrationStates.Revoked
                        : TelegramIntegrationStates.Pending,
                    DetailCode = TelegramIntegrationDetails.AwaitingConfirmation,
                    LinkedAt = link.LinkedAt,
                    LastActivityAt = link.LastActivityAt,
                    LastStatusCheckAt = link.LastStatusCheckAt
                };
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .Include(u => u.GoogleToken)
                .FirstOrDefaultAsync(u => u.Id == link.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (user?.GoogleToken == null)
            {
                return new TelegramCalendarStatusDto
                {
                    Linked = true,
                    GoogleAuthorized = false,
                    AccessTokenExpired = false,
                    HasRequiredScope = false,
                    PermissionScope = null,
                    State = TelegramIntegrationStates.Linked,
                    DetailCode = TelegramIntegrationDetails.GoogleMissing,
                    LinkedAt = link.LinkedAt,
                    LastActivityAt = link.LastActivityAt,
                    LastStatusCheckAt = link.LastStatusCheckAt
                };
            }

            var token = user.GoogleToken;
            var accessExpired = token.AccessTokenExpiresAt.HasValue && token.AccessTokenExpiresAt.Value <= DateTime.UtcNow;
            var revoked = token.RevokedAt.HasValue && (!token.ConsentGrantedAt.HasValue || token.RevokedAt >= token.ConsentGrantedAt);
            var hasScope = !string.IsNullOrWhiteSpace(token.Scope) && token.Scope.Split(' ').Any(s => s.Contains("calendar", StringComparison.OrdinalIgnoreCase));
            var authorized = token.ConsentGrantedAt.HasValue && !revoked && !string.IsNullOrWhiteSpace(token.RefreshToken);

            return new TelegramCalendarStatusDto
            {
                Linked = true,
                GoogleAuthorized = authorized,
                AccessTokenExpired = accessExpired,
                HasRequiredScope = hasScope,
                PermissionScope = token.Scope,
                State = revoked ? TelegramIntegrationStates.Error : TelegramIntegrationStates.Linked,
                DetailCode = revoked
                    ? TelegramIntegrationDetails.GoogleRevoked
                    : !authorized
                        ? TelegramIntegrationDetails.GoogleMissing
                        : accessExpired
                            ? TelegramIntegrationDetails.TokenExpired
                            : !hasScope
                                ? TelegramIntegrationDetails.GoogleScopeInsufficient
                                : null,
                LinkedAt = link.LinkedAt,
                LastActivityAt = link.LastActivityAt,
                LastStatusCheckAt = link.LastStatusCheckAt
            };
        }
    }
}
