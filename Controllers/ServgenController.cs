using Microsoft.AspNetCore.Mvc;
using YandexSpeech.services;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/servgen")]
    public class ServgenController : ControllerBase
    {
        private const int MaxModeSeconds = 3600;
        private readonly ServerRenderModeService _serverRenderModeService;

        public ServgenController(ServerRenderModeService serverRenderModeService)
        {
            _serverRenderModeService = serverRenderModeService;
        }

        [HttpGet("{seconds:int}")]
        public IActionResult EnableServerMode(int seconds)
        {
            if (seconds <= 0)
            {
                return BadRequest("Seconds must be greater than 0.");
            }

            var cappedSeconds = Math.Min(seconds, MaxModeSeconds);
            var enabledUntil = _serverRenderModeService.EnableFor(TimeSpan.FromSeconds(cappedSeconds));

            return Ok(new
            {
                enabled = true,
                seconds = cappedSeconds,
                enabledUntilUtc = enabledUntil.UtcDateTime
            });
        }

        [HttpGet]
        public IActionResult GetServerModeStatus()
        {
            var enabledUntil = _serverRenderModeService.GetEnabledUntilUtc();
            return Ok(new
            {
                enabled = _serverRenderModeService.IsEnabled(),
                enabledUntilUtc = enabledUntil?.UtcDateTime
            });
        }
    }
}
