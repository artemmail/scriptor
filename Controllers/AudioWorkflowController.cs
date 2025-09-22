// Updated AudioWorkflowController to use the queue manager

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using YandexSpeech.services;

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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var taskId = await _taskManager.EnqueueRecognitionTaskAsync(fileId, userId);
            return Ok(taskId);
        }

        [HttpGet("{taskId}")]
        public async Task<IActionResult> GetStatus(string taskId)
        {
            var dto = await _taskManager.GetTaskStatusAsync(taskId);
            if (dto == null)
                return NotFound("Task not found.");
            return Ok(dto);
        }

        [HttpGet("tasks")]
        public async Task<IActionResult> ListTasks()
        {
            var list = await _taskManager.GetAllTasksAsync();
            return Ok(list);
        }

        [HttpDelete("{taskId}")]
        public async Task<IActionResult> DeleteTask(string taskId)
        {
            var deleted = await _taskManager.DeleteTaskAsync(taskId);
            if (!deleted)
                return NotFound("Task not found.");
            return NoContent();
        }
    }
}
