using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using YandexSpeech.services.Options;
using YandexSpeech.services.Telegram;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public sealed class TelegramWebhookController : ControllerBase
    {
        private const string SecretHeaderName = "X-Telegram-Bot-Api-Secret-Token";

        private readonly TelegramTranscriptionBot _bot;
        private readonly IOptionsMonitor<TelegramBotOptions> _optionsMonitor;
        private readonly ILogger<TelegramWebhookController> _logger;

        public TelegramWebhookController(
            TelegramTranscriptionBot bot,
            IOptionsMonitor<TelegramBotOptions> optionsMonitor,
            ILogger<TelegramWebhookController> logger)
        {
            _bot = bot;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> HandleUpdate([FromBody] Update? update, CancellationToken cancellationToken)
        {
            var options = _optionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                return NotFound();
            }

            var providedSecret = Request.Headers[SecretHeaderName].ToString();
            if (!_bot.ValidateSecretToken(providedSecret))
            {
                _logger.LogWarning("Rejected Telegram webhook due to invalid secret token.");
                return Unauthorized();
            }

            if (!_bot.IsReady)
            {
                _logger.LogWarning("Telegram bot is not ready to process webhook updates.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (update is null)
            {
                return Ok();
            }

            await _bot.ProcessUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            return Ok();
        }
    }
}
