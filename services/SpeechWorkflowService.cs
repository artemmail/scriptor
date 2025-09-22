// SpeechWorkflowService.cs

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public class SpeechWorkflowService : ISpeechWorkflowService
    {
        private readonly MyDbContext _db;
        private readonly IYSpeechService _ySpeech;
        private readonly IPunctuationService _punctuation;
        private readonly IAudioFileService _audioFiles;
        private readonly string _bucketName = "ruticker";

        public SpeechWorkflowService(
            MyDbContext dbContext,
            IYSpeechService ySpeech,
            IPunctuationService punctuation,
            IAudioFileService audioFileService
        )
        {
            _db = dbContext;
            _ySpeech = ySpeech;
            _punctuation = punctuation;
            _audioFiles = audioFileService;
        }

        public async Task<AudioWorkflowTask> StartRecognitionTaskAsync(string fileId, string createdBy)
        {
            var file = await _db.AudioFiles.FindAsync(fileId)
                       ?? throw new InvalidOperationException("AudioFile not found");

            // 1) Проверяем, есть ли незавершённая задача для этого файла
            var existing = await _db.AudioWorkflowTasks
                .Where(t => t.AudioFileId == fileId
                            && t.CreatedBy == createdBy
                            && !t.Done
                            && t.Status != RecognizeStatus.Error)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                await ContinueRecognitionAsync(existing.Id);
                return existing;
            }

            // 2) Иначе — создаём новую
            var task = new AudioWorkflowTask
            {
                AudioFileId = file.Id,
                BucketName = _bucketName,
                Status = RecognizeStatus.Created,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedBy = createdBy,
                Done = false
            };
            _db.AudioWorkflowTasks.Add(task);
            await _db.SaveChangesAsync();

            await ContinueRecognitionAsync(task.Id);
            return task;
        }

        public async Task ContinueRecognitionAsync(string taskId)
        {
            var task = await _db.AudioWorkflowTasks.FindAsync(taskId)
                       ?? throw new InvalidOperationException($"Task {taskId} not found");
            try
            {
                switch (task.Status)
                {
                    case RecognizeStatus.Created:
                        await RunConversionAsync(task);
                        break;
                    case RecognizeStatus.Converting:
                        await RunUploadingAsync(task);
                        break;
                    case RecognizeStatus.Uploading:
                        await RunStartRecognitionAsync(task);
                        break;
                    case RecognizeStatus.Recognizing:
                        await RunResultRetrievalAsync(task);
                        break;
                    case RecognizeStatus.RetrievingResult:
                        await RunSegmentingAsync(task);                      
                        break;
                    case RecognizeStatus.SegmentingCaptions:
                    case RecognizeStatus.ApplyingPunctuationSegment:
                        await RunPunctuationOneSegmentStepAsync(task);
                        break;
                    case RecognizeStatus.ApplyingPunctuation:
                        await RunPunctuationBulkAsync(task);
                        break;
                    case RecognizeStatus.Done:
                    case RecognizeStatus.Error:
                        return;
                }
            }
            catch (Exception ex)
            {
                task.Status = RecognizeStatus.Error;
                task.Error = ex.Message;
                task.ModifiedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                throw;
            }
        }

        private async Task RunConversionAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.Converting;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var audioFile = await _db.AudioFiles.FindAsync(task.AudioFileId)
                            ?? throw new InvalidOperationException("AudioFile not found");
            await _audioFiles.ConvertToOpusAsync(audioFile.Id);

            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunUploadingAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.Uploading;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var audioFile = await _db.AudioFiles.FindAsync(task.AudioFileId)!;
            task.ObjectKey ??= Path.GetFileName(audioFile.ConvertedFilePath!);

            await _ySpeech.UploadFileToBucketAsync(
                audioFile.ConvertedFilePath!,
                task.BucketName,
                task.ObjectKey!
            );

            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunStartRecognitionAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.Recognizing;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var uri = $"https://storage.yandexcloud.net/{task.BucketName}/{task.ObjectKey}";
            var res = await _ySpeech.Recognize(uri);
            task.OperationId = res.id;

            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunResultRetrievalAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.RetrievingResult;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var check = await _ySpeech.getRes(task.OperationId!);
            while (!check.done)
            {
                await Task.Delay(4000);
                check = await _ySpeech.getRes(task.OperationId!);
            }

            var segments = TextSegmenter.SplitIntoSegments(check);
            task.RecognizedText = JsonConvert.SerializeObject(segments);
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunSegmentingAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.SegmentingCaptions;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Удаляем старые сегменты (если были)
            _db.AudioWorkflowSegments.RemoveRange(
                _db.AudioWorkflowSegments.Where(s => s.TaskId == task.Id)
            );
            await _db.SaveChangesAsync();

            var raw = JsonConvert.DeserializeObject<string[]>(task.RecognizedText ?? "[]")!;
            int ord = 0;
            var segments = raw.Select(text => new AudioWorkflowSegment
            {
                TaskId = task.Id,
                Order = ord++,
                Text = text,
                IsProcessed = false,
                IsProcessing = false
            }).ToList();

            await _db.AudioWorkflowSegments.AddRangeAsync(segments);
            task.SegmentsTotal = segments.Count;
            task.SegmentsProcessed = 0;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunPunctuationOneSegmentStepAsync(AudioWorkflowTask task)
        {
            // Сброс «залипших» сегментов, если сервис упал между IsProcessing и IsProcessed
            var stuck = _db.AudioWorkflowSegments
                .Where(s => s.TaskId == task.Id && s.IsProcessing && !s.IsProcessed);
            foreach (var s in stuck) s.IsProcessing = false;
            await _db.SaveChangesAsync();

            // Выбираем следующий не-обработанный сегмент
            var segment = await _db.AudioWorkflowSegments
                .Where(s => s.TaskId == task.Id && !s.IsProcessed && !s.IsProcessing)
                .OrderBy(s => s.Order)
                .FirstOrDefaultAsync();

            if (segment == null)
            {
                await CompleteTaskAsync(task);
                return;
            }

            segment.IsProcessing = true;
            await _db.SaveChangesAsync();

            try
            {
                segment.ProcessedText = await _punctuation.FixPunctuationAsync(segment.Text, null);
            }
            catch
            {
                segment.ProcessedText = segment.Text;
            }

            segment.IsProcessed = true;
            segment.IsProcessing = false;
            task.SegmentsProcessed++;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task RunPunctuationBulkAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.ApplyingPunctuation;
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var rawSegments = JsonConvert.DeserializeObject<string[]>(task.RecognizedText ?? "[]")!;
            var processed = new List<string>();
            foreach (var text in rawSegments)
            {
                try { processed.Add(await _punctuation.FixPunctuationAsync(text, null)); }
                catch { processed.Add(text); }
            }

            task.Result = string.Join(" ", processed);
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        private async Task CompleteTaskAsync(AudioWorkflowTask task)
        {
            task.Status = RecognizeStatus.Done;
            task.Done = true;

            var ordered = await _db.AudioWorkflowSegments
                .Where(s => s.TaskId == task.Id && s.IsProcessed)
                .OrderBy(s => s.Order)
                .Select(s => s.ProcessedText!)
                .ToListAsync();

            // Правильная склейка: каждый сегмент с новой строки
            task.Result = string.Join("\n", ordered);
            task.Preview = string.Join(" ", task.Result.Split(' ').Take(100));
            task.ModifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
