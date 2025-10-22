using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.GoogleCalendar
{
    public class GoogleCalendarTokenService : IGoogleCalendarTokenService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<GoogleCalendarTokenService> _logger;

        public GoogleCalendarTokenService(
            UserManager<ApplicationUser> userManager,
            ILogger<GoogleCalendarTokenService> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<GoogleCalendarConsentResult> UpdateConsentAsync(
            ApplicationUser user,
            bool consentGranted,
            IEnumerable<AuthenticationToken>? tokens,
            CancellationToken cancellationToken = default)
        {
            if (!consentGranted)
            {
                return await RevokeConsentAsync(user, cancellationToken);
            }

            return await StoreConsentAsync(user, tokens, cancellationToken);
        }

        private async Task<GoogleCalendarConsentResult> RevokeConsentAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var updated = false;

            if (user.GoogleCalendarConsentAt != null)
            {
                user.GoogleCalendarConsentAt = null;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(user.GoogleAccessToken))
            {
                user.GoogleAccessToken = null;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(user.GoogleRefreshToken))
            {
                user.GoogleRefreshToken = null;
                updated = true;
            }

            if (user.GoogleAccessTokenExpiresAt.HasValue)
            {
                user.GoogleAccessTokenExpiresAt = null;
                updated = true;
            }

            if (user.GoogleRefreshTokenExpiresAt.HasValue)
            {
                user.GoogleRefreshTokenExpiresAt = null;
                updated = true;
            }

            if (!user.GoogleTokensRevokedAt.HasValue ||
                Math.Abs((now - user.GoogleTokensRevokedAt.Value).TotalSeconds) >= 1)
            {
                user.GoogleTokensRevokedAt = now;
                updated = true;
            }

            user.GoogleAccessTokenUpdatedAt = now;

            if (!updated)
            {
                return new GoogleCalendarConsentResult(true, false);
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError(
                    "Failed to revoke Google Calendar tokens for user {UserId}: {Errors}",
                    user.Id,
                    errors);
                return new GoogleCalendarConsentResult(false, false, "Не удалось сохранить изменения профиля.");
            }

            return new GoogleCalendarConsentResult(true, true);
        }

        private async Task<GoogleCalendarConsentResult> StoreConsentAsync(
            ApplicationUser user,
            IEnumerable<AuthenticationToken>? tokens,
            CancellationToken cancellationToken)
        {
            var tokenList = tokens?.ToList() ?? new List<AuthenticationToken>();

            var accessToken = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase))?.Value;
            var refreshToken = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "refresh_token", StringComparison.OrdinalIgnoreCase))?.Value;
            var expiresAtRaw = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "expires_at", StringComparison.OrdinalIgnoreCase))?.Value
                ?? tokenList
                    .FirstOrDefault(t => string.Equals(t.Name, "expires_on", StringComparison.OrdinalIgnoreCase))?.Value;
            var expiresInRaw = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "expires_in", StringComparison.OrdinalIgnoreCase))?.Value;
            var refreshExpiresRaw = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "refresh_token_expires_at", StringComparison.OrdinalIgnoreCase))?.Value;
            var refreshExpiresInRaw = tokenList
                .FirstOrDefault(t => string.Equals(t.Name, "refresh_token_expires_in", StringComparison.OrdinalIgnoreCase))?.Value;

            if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(user.GoogleAccessToken))
            {
                return new GoogleCalendarConsentResult(false, false, "Google не вернул access token.");
            }

            if (string.IsNullOrWhiteSpace(refreshToken) && string.IsNullOrWhiteSpace(user.GoogleRefreshToken))
            {
                return new GoogleCalendarConsentResult(false, false, "Google не вернул refresh token.");
            }

            var now = DateTime.UtcNow;
            var updated = false;

            if (!user.GoogleCalendarConsentAt.HasValue ||
                Math.Abs((now - user.GoogleCalendarConsentAt.Value).TotalSeconds) >= 1)
            {
                user.GoogleCalendarConsentAt = now;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(accessToken) &&
                !string.Equals(user.GoogleAccessToken, accessToken, StringComparison.Ordinal))
            {
                user.GoogleAccessToken = accessToken;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(refreshToken) &&
                !string.Equals(user.GoogleRefreshToken, refreshToken, StringComparison.Ordinal))
            {
                user.GoogleRefreshToken = refreshToken;
                updated = true;
            }

            var accessTokenExpiresAt = ParseExpiration(expiresAtRaw, expiresInRaw, now);
            if (accessTokenExpiresAt.HasValue &&
                (!user.GoogleAccessTokenExpiresAt.HasValue || user.GoogleAccessTokenExpiresAt.Value != accessTokenExpiresAt.Value))
            {
                user.GoogleAccessTokenExpiresAt = accessTokenExpiresAt.Value;
                updated = true;
            }

            var refreshTokenExpiresAt = ParseExpiration(refreshExpiresRaw, refreshExpiresInRaw, now);
            if (refreshTokenExpiresAt.HasValue &&
                (!user.GoogleRefreshTokenExpiresAt.HasValue || user.GoogleRefreshTokenExpiresAt.Value != refreshTokenExpiresAt.Value))
            {
                user.GoogleRefreshTokenExpiresAt = refreshTokenExpiresAt.Value;
                updated = true;
            }

            if (user.GoogleTokensRevokedAt != null)
            {
                user.GoogleTokensRevokedAt = null;
                updated = true;
            }

            if (updated || !user.GoogleAccessTokenUpdatedAt.HasValue)
            {
                user.GoogleAccessTokenUpdatedAt = now;
            }

            if (!updated)
            {
                return new GoogleCalendarConsentResult(true, false);
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogError(
                    "Failed to store Google Calendar tokens for user {UserId}: {Errors}",
                    user.Id,
                    errors);
                return new GoogleCalendarConsentResult(false, false, "Не удалось сохранить изменения профиля.");
            }

            return new GoogleCalendarConsentResult(true, true);
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

            if (!string.IsNullOrWhiteSpace(relativeSeconds) &&
                long.TryParse(relativeSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
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
