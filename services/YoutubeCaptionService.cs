using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using YandexSpeech.models.DB;
using YoutubeDownload.Services;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.ClosedCaptions;
using Microsoft.Extensions.Logging;

namespace YandexSpeech.services
{
    public interface IYoutubeCaptionService
    {
        Task StartCaptionTaskAsync(string id, string createdBy);
        Task ContinueCaptionTaskAsync(string id);
        Task UpdateNullTitlesAsync();
    }

    public class YoutubeCaptionService : IYoutubeCaptionService
    {
        private readonly MyDbContext _dbContext;
        private readonly IPunctuationService _punctuationService;
        private readonly CaptionService _captionService;
        private readonly ILogger<YoutubeCaptionService> _logger;
        private readonly IYSubtitlesService _slugService;

        public YoutubeCaptionService(
            MyDbContext dbContext,
            IPunctuationService punctuationService,
            CaptionService captionService,
            IYSubtitlesService slugService,
            ILogger<YoutubeCaptionService> logger)
        {
            _dbContext = dbContext;
            _punctuationService = punctuationService;
            _captionService = captionService;
            _slugService = slugService;
            _logger = logger;
        }

        public async Task StartCaptionTaskAsync(string id, string createdBy)
        {
            var task = new YoutubeCaptionTask
            {
                Id = id,
                Result = string.Empty,
                Status = RecognizeStatus.Created,
                Done = false,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Preview = string.Empty,
                SegmentsTotal = 0,
                SegmentsProcessed = 0
            };
            _dbContext.YoutubeCaptionTasks.Add(task);
            await _dbContext.SaveChangesAsync();

            task.Status = RecognizeStatus.FetchingMetadata;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await ContinueCaptionTaskAsync(id);
        }

        public async Task ContinueCaptionTaskAsync(string id)
        {
            try
            {
                var taskStatus = await _dbContext.YoutubeCaptionTasks
                    .Where(t => t.Id == id)
                    .Select(t => new { t.Status, t.Error })
                    .FirstOrDefaultAsync();

                if (taskStatus == null)
                {
                    _logger.LogError($"Не найдена задача с Id={id}");
                    return;
                }

                if (taskStatus.Status == RecognizeStatus.Done)
                {
                    _logger.LogInformation($"Задача {id} уже завершена.");
                    return;
                }

                switch (taskStatus.Status)
                {
                    
                    case RecognizeStatus.Created:
                    case RecognizeStatus.FetchingMetadata:
                        await RunFetchingSubtitlesStepAsync(id);
                        goto case RecognizeStatus.DownloadingCaptions;


                    case RecognizeStatus.DownloadingCaptions:
                        await RunDownloadingCaptionsStepAsync(id);
                        break;

                        /*
                    case RecognizeStatus.SegmentingCaptions:
                        await RunSegmentingCaptionsStepAsync(id);
                        break;

                    case RecognizeStatus.ApplyingPunctuation:
                    case RecognizeStatus.ApplyingPunctuationSegment:
                        await RunPunctuationOneSegmentStepAsync(id);
                        break;

                    case RecognizeStatus.Error:
                        _logger.LogError($"Задача {id} находится в состоянии Error: {taskStatus.Error}");
                        break;
                        */
                    default:
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при продолжении задачи {id}");
                await SetTaskErrorAsync(id, ex.Message);
            }
        }

        private async Task SetTaskErrorAsync(string id, string errorMessage)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task != null)
            {
                task.Status = RecognizeStatus.Error;
                task.Error = errorMessage;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task UpdateNullTitlesAsync()
        {
            var tasksWithNullDate = await _dbContext.YoutubeCaptionTasks
                .Where(t => t.UploadDate == null)
                .Select(t => t.Id)
                .ToListAsync();

            foreach (var taskId in tasksWithNullDate)
            {
                try
                {
                    var info = await _captionService.GetVideoInfoAsync(taskId);
                    var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == taskId);
                    if (task != null)
                    {
                        task.UploadDate = info.UploadDate?.UtcDateTime;
                        task.ModifiedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating title for {Id}", taskId);
                }
            }
        }

        private async Task RunFetchingSubtitlesStepAsync(string id)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) throw new InvalidOperationException($"Task {id} not found");

            task.Status = RecognizeStatus.FetchingMetadata;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var info = await _captionService.GetVideoInfoAsync(id);
            task.Title = info.Title;
            task.UploadDate = info.UploadDate?.UtcDateTime;
            task.Slug = _slugService.GenerateSlug(info.Title);
            task.ChannelName = info.Author.ChannelTitle;
            task.ChannelId = info.Author.ChannelId;
            await _dbContext.SaveChangesAsync();

            task.Status = RecognizeStatus.DownloadingCaptions;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task RunDownloadingCaptionsStepAsync(string id)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return;

            task.Status = RecognizeStatus.DownloadingCaptions;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var existing = await _dbContext.YoutubeCaptionTexts.FindAsync(id);
            if (existing == null)
            {
                var caps = await _captionService.GetCaptionsAsync(id);
                var serialized = JsonConvert.SerializeObject(caps);
                _dbContext.YoutubeCaptionTexts.Add(new YoutubeCaptionText { Id = id, Caption = serialized });
                await _dbContext.SaveChangesAsync();
            }

            task.Status = RecognizeStatus.SegmentingCaptions;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task RunSegmentingCaptionsStepAsync(string id)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return;

            task.Status = RecognizeStatus.SegmentingCaptions;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var textEntity = await _dbContext.YoutubeCaptionTexts.AsNoTracking().FirstOrDefaultAsync(ct => ct.Id == id);
            var captions = JsonConvert.DeserializeObject<List<ClosedCaption>>(textEntity!.Caption)!
                              .ToList();

            // Удаляем старые сегменты
            var old = _dbContext.RecognizedSegments.Where(r => r.YoutubeCaptionTaskId == id);
            _dbContext.RecognizedSegments.RemoveRange(old);
            await _dbContext.SaveChangesAsync();

            var processor = new CaptionProcessor();

            var segmentBlockSize = await GetSegmentBlockSizeAsync(RecognitionProfileNames.PunctuationOnly);
            var maxWordsInSegment = segmentBlockSize.HasValue && segmentBlockSize.Value > 0
                ? segmentBlockSize.Value
                : int.MaxValue;
            var windowSize = 400;
            if (segmentBlockSize.HasValue && segmentBlockSize.Value > 0)
            {
                windowSize = Math.Max(1, Math.Min(400, segmentBlockSize.Value));
            }

            var segments = processor.SegmentCaptions(
                captions,
                maxWordsInSegment: maxWordsInSegment,
                windowSize: windowSize,
                pauseThreshold: 1.0);

            int order = 0;
            var newSegments = segments.Select(s => new RecognizedSegment
            {
                YoutubeCaptionTaskId = id,
                Order = order++,
                Text = s,
                IsProcessed = false,
                IsProcessing = false
            });

            await _dbContext.RecognizedSegments.AddRangeAsync(newSegments);
            task.SegmentsTotal = segments.Count;
            task.SegmentsProcessed = 0;
            task.Status = RecognizeStatus.ApplyingPunctuationSegment;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        private async Task RunPunctuationOneSegmentStepAsync(string id)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return;

            // Атомарно резервируем сегмент
            var next = await _dbContext.RecognizedSegments
                .Where(rs => rs.YoutubeCaptionTaskId == id && !rs.IsProcessed && !rs.IsProcessing)
                .OrderBy(rs => rs.Order)
                .FirstOrDefaultAsync();

            if (next == null)
            {
                await CompleteTaskAsync(id);
                return;
            }

            next.IsProcessing = true;
            await _dbContext.SaveChangesAsync();

            // Собираем предыдущий контекст
            var prev = await _dbContext.RecognizedSegments
                .Where(rs => rs.YoutubeCaptionTaskId == id && rs.Order < next.Order && rs.IsProcessed)
                .OrderBy(rs => rs.Order)
                .Select(rs => rs.ProcessedText)
                .ToListAsync();
            var previousContext = string.Join("\n", prev);

            string processed;
            try
            {
                processed = await _punctuationService.FixPunctuationAsync(
                    next.Text,
                    previousContext,
                    RecognitionProfileNames.PunctuationOnly);
            }
            catch
            {
                processed = next.Text;
            }

            next.ProcessedText = processed;
            next.IsProcessed = true;
            next.IsProcessing = false;
            task.SegmentsProcessed++;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Проверяем есть ли еще
            var anyLeft = await _dbContext.RecognizedSegments
                .AnyAsync(rs => rs.YoutubeCaptionTaskId == id && !rs.IsProcessed);
            if (!anyLeft)
                await CompleteTaskAsync(id);
        }

        private async Task CompleteTaskAsync(string id)
        {
            var task = await _dbContext.YoutubeCaptionTasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null) return;

            task.Status = RecognizeStatus.Done;
            task.Done = true;

            var processed = await _dbContext.RecognizedSegments
                .Where(rs => rs.YoutubeCaptionTaskId == id && rs.IsProcessed)
                .OrderBy(rs => rs.Order)
                .Select(rs => rs.ProcessedText)
                .ToListAsync();

            task.Result = string.Join("\n", processed);
            task.Preview = CreatePreview(task.Result, 100);
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _slugService.NotifyYandexAsync("youscriptor.com", "f59e3d2c25e394fb", task.Slug);
        }

        private async Task<int?> GetSegmentBlockSizeAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return null;

            var blockSize = await _dbContext.RecognitionProfiles
                .AsNoTracking()
                .Where(p => p.Name == profileName)
                .Select(p => (int?)p.SegmentBlockSize)
                .FirstOrDefaultAsync();

            if (blockSize is null)
            {
                _logger.LogWarning(
                    "Recognition profile {ProfileName} not found while determining segment block size.",
                    profileName);
            }

            return blockSize;
        }

        private string CreatePreview(string text, int wordCount)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var words = text.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Take(wordCount));
        }
    }
}
