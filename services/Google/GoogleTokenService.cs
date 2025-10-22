using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Google
{
    public class GoogleTokenService : IGoogleTokenService
    {
        private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");

        private readonly MyDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GoogleTokenService> _logger;
        private readonly string? _clientId;
        private readonly string? _clientSecret;
        private readonly HashSet<string> _calendarScopes;

        public GoogleTokenService(
            MyDbContext dbContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<GoogleTokenService> logger)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            var googleSection = configuration.GetSection("Authentication:Google");
            _clientId = googleSection["ClientId"];
            _clientSecret = googleSection["ClientSecret"];

            var configuredScopes = configuration
                .GetSection("Authentication:GoogleCalendar:Scopes")
                .Get<string[]>() ?? Array.Empty<string>();

            if (configuredScopes.Length == 0)
            {
                configuredScopes = new[]
                {
                    "https://www.googleapis.com/auth/calendar.events",
                    "https://www.googleapis.com/auth/calendar"
                };
            }

            _calendarScopes = new HashSet<string>(configuredScopes
                .Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> HasCalendarAccessAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var token = await _dbContext.UserGoogleTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar,
                    cancellationToken);

            if (token == null || token.RevokedAt.HasValue)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return false;
            }

            if (token.AccessTokenExpiresAt.HasValue && token.AccessTokenExpiresAt.Value <= DateTime.UtcNow)
            {
                return false;
            }

            return true;
        }

        public Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default)
            => EnsureAccessTokenAsync(user, consentGranted: false, tokens: null, cancellationToken);

        public async Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(
            ApplicationUser user,
            bool consentGranted,
            IEnumerable<AuthenticationToken>? tokens,
            CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var tokenEntity = await _dbContext.UserGoogleTokens
                .FirstOrDefaultAsync(
                    t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar,
                    cancellationToken);

            var tokenList = tokens?.ToList() ?? new List<AuthenticationToken>();

            if (tokenList.Count == 0)
            {
                if (tokenEntity == null)
                {
                    return new GoogleTokenOperationResult(
                        false,
                        false,
                        null,
                        null,
                        "Сохранённых токенов Google не найдено.");
                }

                return await EnsureValidAccessTokenAsync(tokenEntity, cancellationToken);
            }

            var now = DateTime.UtcNow;
            var scopes = ExtractScopes(tokenList);
            var hasCalendarScope = scopes.Overlaps(_calendarScopes);
            var shouldPersist = hasCalendarScope || consentGranted;
            var hasStoredTokens = tokenEntity != null && (
                !string.IsNullOrWhiteSpace(tokenEntity.AccessToken) ||
                !string.IsNullOrWhiteSpace(tokenEntity.RefreshToken));

            if (!shouldPersist)
            {
                if (!hasStoredTokens)
                {
                    return new GoogleTokenOperationResult(true, false, tokenEntity?.AccessToken, tokenEntity, null);
                }

                return await EnsureValidAccessTokenAsync(tokenEntity!, cancellationToken);
            }

            var created = false;
            if (tokenEntity == null)
            {
                tokenEntity = new UserGoogleToken
                {
                    UserId = user.Id,
                    TokenType = GoogleTokenTypes.Calendar,
                    CreatedAt = now
                };
                _dbContext.UserGoogleTokens.Add(tokenEntity);
                created = true;
            }

            var accessToken = FindTokenValue(tokenList, "access_token");
            var refreshToken = FindTokenValue(tokenList, "refresh_token");
            var expiresAtRaw = FindTokenValue(tokenList, "expires_at") ?? FindTokenValue(tokenList, "expires_on");
            var expiresInRaw = FindTokenValue(tokenList, "expires_in");
            var refreshExpiresAtRaw = FindTokenValue(tokenList, "refresh_token_expires_at");
            var refreshExpiresInRaw = FindTokenValue(tokenList, "refresh_token_expires_in");

            if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(tokenEntity.AccessToken))
            {
                return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Google не вернул access token.");
            }

            if (string.IsNullOrWhiteSpace(refreshToken) && string.IsNullOrWhiteSpace(tokenEntity.RefreshToken))
            {
                return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Google не вернул refresh token.");
            }

            var accessTokenExpiresAt = ParseExpiration(expiresAtRaw, expiresInRaw, now);
            var refreshTokenExpiresAt = ParseExpiration(refreshExpiresAtRaw, refreshExpiresInRaw, now);

            var updated = created;

            if (scopes.Count > 0)
            {
                var scopeJoined = string.Join(" ", scopes);
                if (!string.Equals(tokenEntity.Scope, scopeJoined, StringComparison.Ordinal))
                {
                    tokenEntity.Scope = scopeJoined;
                    updated = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(accessToken) && !string.Equals(tokenEntity.AccessToken, accessToken, StringComparison.Ordinal))
            {
                tokenEntity.AccessToken = accessToken;
                tokenEntity.AccessTokenUpdatedAt = now;
                updated = true;
            }
            else if (!string.IsNullOrWhiteSpace(tokenEntity.AccessToken) && tokenEntity.AccessTokenUpdatedAt == null)
            {
                tokenEntity.AccessTokenUpdatedAt = now;
                updated = true;
            }

            if (accessTokenExpiresAt.HasValue && tokenEntity.AccessTokenExpiresAt != accessTokenExpiresAt)
            {
                tokenEntity.AccessTokenExpiresAt = accessTokenExpiresAt;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(refreshToken) && !string.Equals(tokenEntity.RefreshToken, refreshToken, StringComparison.Ordinal))
            {
                tokenEntity.RefreshToken = refreshToken;
                updated = true;
            }

            if (refreshTokenExpiresAt.HasValue && tokenEntity.RefreshTokenExpiresAt != refreshTokenExpiresAt)
            {
                tokenEntity.RefreshTokenExpiresAt = refreshTokenExpiresAt;
                updated = true;
            }

            if (tokenEntity.RevokedAt != null)
            {
                tokenEntity.RevokedAt = null;
                updated = true;
            }

            if ((consentGranted || hasCalendarScope) && (!tokenEntity.ConsentGrantedAt.HasValue || (now - tokenEntity.ConsentGrantedAt.Value).TotalSeconds >= 1))
            {
                tokenEntity.ConsentGrantedAt = now;
                updated = true;
            }

            if ((consentGranted || hasCalendarScope) && tokenEntity.ConsentDeclinedAt.HasValue)
            {
                tokenEntity.ConsentDeclinedAt = null;
                updated = true;
            }

            if (updated)
            {
                tokenEntity.UpdatedAt = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new GoogleTokenOperationResult(true, updated, tokenEntity.AccessToken, tokenEntity, null);
        }

        public async Task RecordCalendarDeclinedAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var tokenEntity = await _dbContext.UserGoogleTokens
                .FirstOrDefaultAsync(
                    t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar,
                    cancellationToken);

            var now = DateTime.UtcNow;

            if (tokenEntity == null)
            {
                tokenEntity = new UserGoogleToken
                {
                    UserId = user.Id,
                    TokenType = GoogleTokenTypes.Calendar,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConsentDeclinedAt = now
                };
                _dbContext.UserGoogleTokens.Add(tokenEntity);
            }
            else
            {
                tokenEntity.ConsentDeclinedAt = now;
                tokenEntity.UpdatedAt = now;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<GoogleTokenOperationResult> RevokeAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var tokenEntity = await _dbContext.UserGoogleTokens
                .FirstOrDefaultAsync(
                    t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar,
                    cancellationToken);

            if (tokenEntity == null)
            {
                return new GoogleTokenOperationResult(true, false, null, null, null);
            }

            var updated = false;
            var now = DateTime.UtcNow;

            if (tokenEntity.AccessToken != null)
            {
                tokenEntity.AccessToken = null;
                updated = true;
            }

            if (tokenEntity.RefreshToken != null)
            {
                tokenEntity.RefreshToken = null;
                updated = true;
            }

            if (tokenEntity.AccessTokenExpiresAt.HasValue)
            {
                tokenEntity.AccessTokenExpiresAt = null;
                updated = true;
            }

            if (tokenEntity.AccessTokenUpdatedAt.HasValue)
            {
                tokenEntity.AccessTokenUpdatedAt = null;
                updated = true;
            }

            if (tokenEntity.RefreshTokenExpiresAt.HasValue)
            {
                tokenEntity.RefreshTokenExpiresAt = null;
                updated = true;
            }

            if (!tokenEntity.RevokedAt.HasValue || (now - tokenEntity.RevokedAt.Value).TotalSeconds >= 1)
            {
                tokenEntity.RevokedAt = now;
                updated = true;
            }

            if (updated)
            {
                tokenEntity.UpdatedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new GoogleTokenOperationResult(true, updated, null, tokenEntity, null);
        }

        private async Task<GoogleTokenOperationResult> EnsureValidAccessTokenAsync(
            UserGoogleToken tokenEntity,
            CancellationToken cancellationToken)
        {
            if (tokenEntity.RevokedAt.HasValue && (!tokenEntity.ConsentGrantedAt.HasValue || tokenEntity.RevokedAt.Value >= tokenEntity.ConsentGrantedAt.Value))
            {
                return new GoogleTokenOperationResult(false, false, null, tokenEntity, "Доступ к Google Calendar был отозван.");
            }

            var now = DateTime.UtcNow;
            var hasValidToken = !string.IsNullOrWhiteSpace(tokenEntity.AccessToken)
                && (!tokenEntity.AccessTokenExpiresAt.HasValue || tokenEntity.AccessTokenExpiresAt.Value > now.AddMinutes(1));

            if (hasValidToken)
            {
                return new GoogleTokenOperationResult(true, false, tokenEntity.AccessToken, tokenEntity, null);
            }

            if (string.IsNullOrWhiteSpace(tokenEntity.RefreshToken))
            {
                return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Отсутствует refresh token для обновления доступа.");
            }

            return await RefreshAccessTokenAsync(tokenEntity, cancellationToken);
        }

        private async Task<GoogleTokenOperationResult> RefreshAccessTokenAsync(
            UserGoogleToken tokenEntity,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            {
                _logger.LogWarning("Google OAuth client credentials are not configured. Unable to refresh access token for user {UserId}.", tokenEntity.UserId);
                return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Не настроены параметры Google OAuth.");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId!,
                    ["client_secret"] = _clientSecret!,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = tokenEntity.RefreshToken!
                });

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to refresh Google access token for user {UserId}. Status: {Status}. Body: {Body}",
                        tokenEntity.UserId,
                        response.StatusCode,
                        body);
                    return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Не удалось обновить access token Google.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: cancellationToken);

                if (!payload.TryGetProperty("access_token", out var accessTokenProp))
                {
                    return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Google не вернул access token.");
                }

                var accessToken = accessTokenProp.GetString();
                var now = DateTime.UtcNow;

                long? expiresInSeconds = null;
                if (payload.TryGetProperty("expires_in", out var expiresInProp) && expiresInProp.ValueKind == JsonValueKind.Number)
                {
                    if (expiresInProp.TryGetInt64(out var expiresLong))
                    {
                        expiresInSeconds = expiresLong;
                    }
                    else if (expiresInProp.TryGetDouble(out var expiresDouble))
                    {
                        expiresInSeconds = (long)expiresDouble;
                    }
                }

                string? refreshToken = null;
                if (payload.TryGetProperty("refresh_token", out var refreshTokenProp))
                {
                    refreshToken = refreshTokenProp.GetString();
                }

                string? scope = null;
                if (payload.TryGetProperty("scope", out var scopeProp))
                {
                    scope = scopeProp.GetString();
                }

                long? refreshExpiresInSeconds = null;
                if (payload.TryGetProperty("refresh_token_expires_in", out var refreshExpiresInProp) && refreshExpiresInProp.ValueKind == JsonValueKind.Number)
                {
                    if (refreshExpiresInProp.TryGetInt64(out var refreshExpiresLong))
                    {
                        refreshExpiresInSeconds = refreshExpiresLong;
                    }
                    else if (refreshExpiresInProp.TryGetDouble(out var refreshExpiresDouble))
                    {
                        refreshExpiresInSeconds = (long)refreshExpiresDouble;
                    }
                }

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Google не вернул access token.");
                }

                tokenEntity.AccessToken = accessToken;
                tokenEntity.AccessTokenUpdatedAt = now;
                tokenEntity.AccessTokenExpiresAt = expiresInSeconds.HasValue
                    ? now.AddSeconds(Math.Max(0, expiresInSeconds.Value))
                    : tokenEntity.AccessTokenExpiresAt;

                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    tokenEntity.RefreshToken = refreshToken;
                }

                if (refreshExpiresInSeconds.HasValue)
                {
                    tokenEntity.RefreshTokenExpiresAt = now.AddSeconds(Math.Max(0, refreshExpiresInSeconds.Value));
                }

                if (!string.IsNullOrWhiteSpace(scope))
                {
                    tokenEntity.Scope = scope;
                }

                tokenEntity.RevokedAt = null;
                tokenEntity.UpdatedAt = now;

                await _dbContext.SaveChangesAsync(cancellationToken);

                return new GoogleTokenOperationResult(true, true, tokenEntity.AccessToken, tokenEntity, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogError(ex, "Unexpected error while refreshing Google access token for user {UserId}", tokenEntity.UserId);
                return new GoogleTokenOperationResult(false, false, tokenEntity.AccessToken, tokenEntity, "Ошибка при обновлении access token Google.");
            }
        }

        private static string? FindTokenValue(IEnumerable<AuthenticationToken> tokens, string name)
        {
            return tokens.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static HashSet<string> ExtractScopes(IEnumerable<AuthenticationToken> tokens)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var token in tokens)
            {
                if (!string.Equals(token.Name, "scope", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(token.Name, "granted_scopes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(token.Value))
                {
                    continue;
                }

                foreach (var part in token.Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    result.Add(part.Trim());
                }
            }

            return result;
        }

        private static DateTime? ParseExpiration(string? absoluteValue, string? relativeSeconds, DateTime nowUtc)
        {
            if (!string.IsNullOrWhiteSpace(absoluteValue))
            {
                if (DateTime.TryParse(
                        absoluteValue,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                }
            }

            if (!string.IsNullOrWhiteSpace(relativeSeconds)
                && long.TryParse(relativeSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                try
                {
                    return nowUtc.AddSeconds(seconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
