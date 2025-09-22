using Microsoft.AspNetCore.Mvc;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;

namespace YandexSpeech.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecognitionController : ControllerBase
{
    private readonly IRecognitionTaskManager _taskManager;

    public RecognitionController(IRecognitionTaskManager taskManager)
    {

//        ISpeechWorkflowService
        _taskManager = taskManager;
    }

    [HttpPost("start")]
    public async Task<ActionResult<string>> StartRecognition([FromQuery] string filePath, [FromQuery] string user)
    {
        

        // Добавляем/проверяем задачу
        var taskId = await _taskManager.EnqueueRecognitionAsync("c:/amd/2.webm", "anonymous");
        return Ok(taskId);
    }


    [HttpGet("run")]
    public async Task<ActionResult<string>> StartRecognition1()
    {


        // Добавляем/проверяем задачу
        var taskId = await _taskManager.EnqueueRecognitionAsync("c:/amd/2.webm", "anonymous");
        return Ok(taskId);
    }


    [HttpGet("{taskId}")]
    public async Task<ActionResult<SpeechRecognitionTaskDto>> GetStatus(string taskId)
    {
        var task = await _taskManager.GetTaskStatusAsync(taskId);
        if (task == null)
            return NotFound("Task not found.");

        var taskDto = new SpeechRecognitionTaskDto(task);
        return Ok(taskDto);
    }

    [HttpGet("all")]
    public async Task<ActionResult<List<SpeechRecognitionTaskDto>>> GetAllTasks()
    {
        var tasks = await _taskManager.GetAllTasksAsync();
        var taskDtos = tasks.Select(task => new SpeechRecognitionTaskDto(task)).ToList();
        return Ok(taskDtos);
    }




    [HttpPost("start-subtitle-recognition")]
    public async Task<ActionResult<string>> StartSubtitleRecognition(
        [FromQuery] string youtubeId,
        [FromQuery] string? language,
        [FromQuery] string? createdBy = "system" // Опционально
    )
    {
        // Вызываем новый метод из TaskManager
        var taskId = await _taskManager.EnqueueSubtitleRecognitionAsync(
            youtubeId,
            language,
            createdBy ?? "system"
        );

        return Ok(taskId);
    }
}
