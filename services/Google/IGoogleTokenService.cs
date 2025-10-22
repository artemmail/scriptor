using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Google
{
    public interface IGoogleTokenService
    {
        Task<bool> HasCalendarAccessAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default);

        Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(
            ApplicationUser user,
            bool consentGranted,
            IEnumerable<AuthenticationToken>? tokens,
            CancellationToken cancellationToken = default);

        Task<GoogleTokenOperationResult> EnsureAccessTokenAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default);

        Task RecordCalendarDeclinedAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default);

        Task<GoogleTokenOperationResult> RevokeAsync(
            ApplicationUser user,
            CancellationToken cancellationToken = default);
    }

    public sealed record GoogleTokenOperationResult(
        bool Succeeded,
        bool Updated,
        string? AccessToken,
        UserGoogleToken? Token,
        string? ErrorMessage);
}
