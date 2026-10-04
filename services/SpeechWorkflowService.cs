using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;

namespace YandexSpeech.services;

public sealed class SpeechWorkflowService : ISpeechWorkflowService
{
    private readonly MyDbContext _db;
    private readonly IAudioFileService _files;
    private readonly IYSpeechService _speech;
    private readonly ILogger<SpeechWorkflowService> _logger;
    private readonly string _bucketName;

    public SpeechWorkflowService(
        MyDbContext db,
        IAudioFileService files,
        IYSpeechService speech,
        IConfiguration configuration,
        ILogger<SpeechWorkflowService> logger)
    {
        _db = db;
        _files = files;
        _speech = speech;
        _logger = logger;
        _bucketName = configuration["YSpeech:DefaultBucketName"]
            ?? throw new InvalidOperationException("YSpeech:DefaultBucketName is not configured.");
    }

    public async Task<AudioWorkflowTask> StartRecognitionTaskAsync(string fileId, string createdBy)
    {
        if (!await _db.AudioFiles.AsNoTracking()
                .AnyAsync(file => file.Id == fileId && file.CreatedBy == createdBy))
            throw new KeyNotFoundException("Audio file not found.");

        var existing = await _db.AudioWorkflowTasks
            .Where(task => task.AudioFileId == fileId && task.CreatedBy == createdBy
                && !task.Done && task.Status != RecognizeStatus.Error)
            .OrderByDescending(task => task.CreatedAt)
            .FirstOrDefaultAsync();
        if (existing != null)
            return existing;

        var now = DateTime.UtcNow;
        var task = new AudioWorkflowTask
        {
            AudioFileId = fileId,
            CreatedBy = createdBy,
            BucketName = _bucketName,
            ObjectKey = $"audio-workflow/{Guid.NewGuid():N}.opus",
            Status = RecognizeStatus.Created,
            CreatedAt = now,
            ModifiedAt = now
        };
        _db.AudioWorkflowTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    public async Task ContinueRecognitionAsync(string taskId)
    {
        var task = await _db.AudioWorkflowTasks
            .Include(item => item.AudioFile)
            .FirstOrDefaultAsync(item => item.Id == taskId);
        if (task == null || task.Done || task.Status == RecognizeStatus.Error)
            return;

        try
        {
            switch (task.Status)
            {
                case RecognizeStatus.Created:
                case RecognizeStatus.Converting:
                    await SetStatusAsync(task, RecognizeStatus.Converting);
                    await _files.ConvertToOpusAsync(task.AudioFileId);
                    await SetStatusAsync(task, RecognizeStatus.Uploading);
                    break;

                case RecognizeStatus.Uploading:
                    var opusPath = task.AudioFile.ConvertedFilePath;
                    if (string.IsNullOrWhiteSpace(opusPath) || !File.Exists(opusPath))
                        throw new FileNotFoundException("Converted audio file is missing.");

                    var operation = await _speech.UploadOpusAndRecognizeAsync(
                        opusPath, task.BucketName, task.ObjectKey);
                    if (string.IsNullOrWhiteSpace(operation?.id))
                        throw new InvalidOperationException("Speech API did not return an operation ID.");
                    task.OperationId = operation.id;
                    await SetStatusAsync(task, RecognizeStatus.Recognizing);
                    break;

                case RecognizeStatus.Recognizing:
                case RecognizeStatus.RetrievingResult:
                    if (string.IsNullOrWhiteSpace(task.OperationId))
                        throw new InvalidOperationException("Recognition operation ID is missing.");

                    var result = await _speech.getRes(task.OperationId);
                    if (result == null)
                        throw new InvalidOperationException("Speech API returned an empty operation.");
                    if (!result.done)
                        return;
                    if (result.error != null)
                        throw new InvalidOperationException(
                            $"Speech API error {result.error.code}: {result.error.message}");

                    var segments = result.response?.chunks?
                        .Select(chunk => chunk.alternatives?.FirstOrDefault()?.text?.Trim())
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Select(text => text!)
                        .ToList() ?? [];
                    if (segments.Count == 0)
                        throw new InvalidOperationException("Speech API returned no recognized text.");

                    task.RecognizedText = string.Join(" ", segments);
                    task.Result = task.RecognizedText;
                    task.Preview = task.Result.Length > 300 ? task.Result[..300] : task.Result;
                    task.SegmentsTotal = segments.Count;
                    task.SegmentsProcessed = segments.Count;
                    for (var index = 0; index < segments.Count; index++)
                    {
                        _db.AudioWorkflowSegments.Add(new AudioWorkflowSegment
                        {
                            TaskId = task.Id,
                            Order = index,
                            Text = segments[index],
                            ProcessedText = segments[index],
                            IsProcessed = true
                        });
                    }
                    task.Done = true;
                    await SetStatusAsync(task, RecognizeStatus.Done);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported audio workflow status: {task.Status}.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio recognition task {TaskId} failed", task.Id);
            task.Error = "Не удалось распознать аудио. Проверьте файл и попробуйте ещё раз.";
            task.Done = true;
            await SetStatusAsync(task, RecognizeStatus.Error);
        }
    }

    private async Task SetStatusAsync(AudioWorkflowTask task, RecognizeStatus status)
    {
        task.Status = status;
        task.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
