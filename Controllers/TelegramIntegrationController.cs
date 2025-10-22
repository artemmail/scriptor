using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DTO.Telegram;
using YandexSpeech.services.Authentication;
using YandexSpeech.services.TelegramIntegration;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public sealed class TelegramIntegrationController : ControllerBase
    {
        private readonly ITelegramLinkService _linkService;
        private readonly ILogger<TelegramIntegrationController> _logger;

        public TelegramIntegrationController(
            ITelegramLinkService linkService,
            ILogger<TelegramIntegrationController> logger)
        {
            _linkService = linkService;
            _logger = logger;
        }

        [Authorize(AuthenticationSchemes = IntegrationApiAuthenticationDefaults.AuthenticationScheme)]
        [HttpPost("link/initiate")]
        public async Task<ActionResult<TelegramLinkInitiateResponse>> InitiateLinkAsync(
            [FromBody] TelegramLinkInitiateRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var context = new TelegramLinkInitiationContext(
                    request.TelegramId,
                    request.Username,
                    request.FirstName,
                    request.LastName,
                    request.LanguageCode);

                var result = await _linkService.CreateLinkTokenAsync(context, cancellationToken).ConfigureAwait(false);
                return new TelegramLinkInitiateResponse
                {
                    Token = result.Token,
                    LinkUrl = result.LinkUrl,
                    ExpiresAt = result.ExpiresAt,
                    Status = result.Status
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate Telegram link for {TelegramId}.", request.TelegramId);
                return StatusCode(500, new { error = "integration_failed" });
            }
        }

        [Authorize]
        [HttpPost("link/confirm")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult<TelegramLinkConfirmationResponse>> ConfirmLinkAsync(
            [FromForm] TelegramLinkConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                var result = await _linkService.ConfirmLinkAsync(request.Token, userId, cancellationToken).ConfigureAwait(false);
                return new TelegramLinkConfirmationResponse
                {
                    Success = result.Success,
                    State = result.State,
                    Status = result.Status,
                    Error = result.Error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to confirm Telegram link for user {UserId}.", userId);
                return StatusCode(500, new { error = "integration_failed" });
            }
        }

        [Authorize(AuthenticationSchemes = IntegrationApiAuthenticationDefaults.AuthenticationScheme)]
        [HttpGet("{telegramId:long}/calendar-status")]
        public async Task<ActionResult<TelegramCalendarStatusResponse>> GetCalendarStatusAsync(long telegramId, CancellationToken cancellationToken)
        {
            try
            {
                var status = await _linkService.GetCalendarStatusAsync(telegramId, cancellationToken).ConfigureAwait(false);
                return new TelegramCalendarStatusResponse
                {
                    Status = status,
                    Refreshed = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to obtain Telegram calendar status for {TelegramId}.", telegramId);
                return StatusCode(500, new { error = "integration_failed" });
            }
        }

        [Authorize(AuthenticationSchemes = IntegrationApiAuthenticationDefaults.AuthenticationScheme)]
        [HttpPost("{telegramId:long}/calendar-status/refresh")]
        public async Task<ActionResult<TelegramCalendarStatusResponse>> RefreshCalendarStatusAsync(long telegramId, CancellationToken cancellationToken)
        {
            try
            {
                var status = await _linkService.RefreshCalendarStatusAsync(telegramId, cancellationToken).ConfigureAwait(false);
                return new TelegramCalendarStatusResponse
                {
                    Status = status,
                    Refreshed = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh Telegram calendar status for {TelegramId}.", telegramId);
                return StatusCode(500, new { error = "integration_failed" });
            }
        }

        [Authorize]
        [HttpPost("link/unlink")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlinkAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var link = await _linkService.FindLinkByUserAsync(userId, cancellationToken).ConfigureAwait(false);
            if (link == null)
            {
                return NotFound();
            }

            await _linkService.UnlinkAsync(link.TelegramId, userId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
    }
}
