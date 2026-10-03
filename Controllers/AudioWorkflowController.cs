// Updated AudioWorkflowController to use the queue manager

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using YandexSpeech.services;
using YandexSpeech.Extensions;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AudioWorkflowController : ControllerBase
    {
        private readonly IAudioTaskManager _taskManager;

        public AudioWorkflowController(IAudioTaskManager taskManager)
        {
            _taskManager = taskManager;
        }

        [HttpPost("{fileId}/recognize")]
        public async Task<ActionResult<string>> StartRecognition(string fileId)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            try
            {
                var taskId = await _taskManager.EnqueueRecognitionTaskAsync(fileId, userId);
                return Ok(taskId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound("Audio file not found.");
            }
            catch (AudioWorkflowUnavailableException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    "Распознавание аудио сейчас недоступно.");
            }
        }

        [HttpGet("{taskId}")]
        public async Task<IActionResult> GetStatus(string taskId)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var dto = await _taskManager.GetTaskStatusAsync(taskId, userId);
            if (dto == null)
                return NotFound("Task not found.");
            return Ok(dto);
        }

        [HttpGet("tasks")]
        public async Task<IActionResult> ListTasks()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var list = await _taskManager.GetAllTasksAsync(userId);
            return Ok(list);
        }

        [HttpDelete("{taskId}")]
        public async Task<IActionResult> DeleteTask(string taskId)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var deleted = await _taskManager.DeleteTaskAsync(taskId, userId);
            if (!deleted)
                return NotFound("Task not found.");
            return NoContent();
        }
    }
}
