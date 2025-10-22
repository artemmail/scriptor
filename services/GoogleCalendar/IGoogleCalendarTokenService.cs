using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.GoogleCalendar
{
    public interface IGoogleCalendarTokenService
    {
        Task<GoogleCalendarConsentResult> UpdateConsentAsync(
            ApplicationUser user,
            bool consentGranted,
            IEnumerable<AuthenticationToken>? tokens,
            CancellationToken cancellationToken = default);
    }

    public sealed record GoogleCalendarConsentResult(
        bool Succeeded,
        bool Updated,
        string? ErrorMessage = null);
}
