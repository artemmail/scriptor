using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace YandexSpeech.models.DTO.Telegram
{
    public sealed class TelegramLinkInitiateRequest
    {
        [Required]
        public long TelegramId { get; set; }

        public string? Username { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public string? LanguageCode { get; set; }
    }

    public sealed class TelegramLinkInitiateResponse
    {
        public string Token { get; set; } = string.Empty;

        public Uri? LinkUrl { get; set; }

        public DateTime ExpiresAt { get; set; }

        public TelegramCalendarStatusDto Status { get; set; } = TelegramCalendarStatusDto.NotLinked();
    }

    public sealed class TelegramLinkConfirmationRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }

    public sealed class TelegramLinkConfirmationResponse
    {
        public bool Success { get; set; }

        public string State { get; set; } = TelegramIntegrationStates.NotLinked;

        public TelegramCalendarStatusDto Status { get; set; } = TelegramCalendarStatusDto.NotLinked();

        public string? Error { get; set; }
    }

    public sealed class TelegramCalendarStatusResponse
    {
        public TelegramCalendarStatusDto Status { get; set; } = TelegramCalendarStatusDto.NotLinked();

        public bool Refreshed { get; set; }
    }

    public sealed class TelegramCalendarEventResponse
    {
        public bool Success { get; set; }

        public string? EventId { get; set; }

        public string? HtmlLink { get; set; }

        public DateTimeOffset? StartsAt { get; set; }

        public DateTimeOffset? EndsAt { get; set; }

        public string? TimeZone { get; set; }

        public string? Error { get; set; }
    }

    public sealed class TelegramCalendarStatusDto
    {
        public bool Linked { get; init; }

        public bool GoogleAuthorized { get; init; }

        public bool AccessTokenExpired { get; init; }

        public bool HasRequiredScope { get; init; }

        public string? PermissionScope { get; init; }

        public string State { get; init; } = TelegramIntegrationStates.NotLinked;

        public string? DetailCode { get; init; }

        public DateTime? LinkedAt { get; init; }

        public DateTime? LastActivityAt { get; init; }

        public DateTime? LastStatusCheckAt { get; init; }

        public string? LastError { get; init; }

        [JsonIgnore]
        public bool HasCalendarAccess => Linked && GoogleAuthorized && !AccessTokenExpired && HasRequiredScope;

        public static TelegramCalendarStatusDto NotLinked() => new()
        {
            Linked = false,
            GoogleAuthorized = false,
            AccessTokenExpired = false,
            HasRequiredScope = false,
            PermissionScope = null,
            State = TelegramIntegrationStates.NotLinked,
            DetailCode = TelegramIntegrationDetails.LinkMissing
        };
    }

    public sealed class TelegramCalendarEventResult
    {
        public bool Success { get; init; }

        public string? EventId { get; init; }

        public string? HtmlLink { get; init; }

        public DateTimeOffset? StartsAt { get; init; }

        public DateTimeOffset? EndsAt { get; init; }

        public string? TimeZone { get; init; }

        public string? Error { get; init; }

        public static TelegramCalendarEventResult Failure(string error) => new()
        {
            Success = false,
            Error = error
        };
    }

    public sealed class TelegramLinkInitiationResult
    {
        public string Token { get; init; } = string.Empty;

        public Uri LinkUrl { get; init; } = new("https://example.com");

        public DateTime ExpiresAt { get; init; }

        public TelegramCalendarStatusDto Status { get; init; } = TelegramCalendarStatusDto.NotLinked();
    }

    public sealed class TelegramLinkConfirmationResult
    {
        public bool Success { get; init; }

        public string State { get; init; } = TelegramIntegrationStates.NotLinked;

        public TelegramCalendarStatusDto Status { get; init; } = TelegramCalendarStatusDto.NotLinked();

        public string? Error { get; init; }
    }

    public static class TelegramIntegrationStates
    {
        public const string NotLinked = "not_linked";
        public const string Pending = "pending";
        public const string Linked = "linked";
        public const string Revoked = "revoked";
        public const string Error = "error";
    }

    public static class TelegramIntegrationDetails
    {
        public const string LinkMissing = "link_missing";
        public const string AwaitingConfirmation = "awaiting_confirmation";
        public const string GoogleMissing = "google_missing";
        public const string GoogleScopeInsufficient = "google_scope_insufficient";
        public const string GoogleRevoked = "google_revoked";
        public const string TokenExpired = "token_expired";
        public const string InternalError = "internal_error";
    }
}
