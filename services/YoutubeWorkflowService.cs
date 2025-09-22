using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeDownload.Models;
using YandexSpeech.services;
using YandexSpeech;

namespace YoutubeDownload.Services
{
    /// <summary>
    /// DTO для списка задач (добавлены FileName, Title, YoutubeId, YoutubeUrl, DownloadUrl).
    /// </summary>
    public class MergedVideoDto
    {
        public string TaskId { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }     // <-- имя файла без пути
        public string? Title { get; set; }        // <-- заголовок видео
        public string? YoutubeId { get; set; }    // <-- id видео на YouTube
        public string? YoutubeUrl { get; set; }   // <-- ссылка на YouTube
        public string? DownloadUrl { get; set; }  // <-- ссылка на скачивание с бэка (api)
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
    }

    /// <summary>
    /// DTO для статуса задачи (для pollProgress) — добавили ссылку и имя файла при Done.
    /// </summary>
    public class TaskStatusDto
    {
        public string Status { get; set; }     // Created/Downloading/Merging/Done/Error
        public int Progress { get; set; }      // 0..100
        public string? Error { get; set; }
        public string? DownloadUrl { get; set; } // при Done
        public string? FileName { get; set; }    // при Done
    }

    /// <summary>
    /// Сервис полного цикла скачивания и мерджа YouTube-видео + выдача субтитров SRT по taskId.
    /// </summary>
    public class YoutubeWorkflowService
    {
        private readonly MyDbContext _dbContext;
        private readonly YoutubeStreamService _youtubeStreamService;
        private readonly YoutubeClient _youtubeClient;
        private readonly string _defaultSaveDir;

        public YoutubeWorkflowService(
            MyDbContext dbContext,
            YoutubeStreamService youtubeStreamService,
            YoutubeClient youtubeClient)
        {
            _dbContext = dbContext;
            _youtubeStreamService = youtubeStreamService;
            _youtubeClient = youtubeClient;
            _defaultSaveDir = @"C:\Temp";
        }

        /// <summary>
        /// Создаёт задачу (сохраняем также VideoTitle).
        /// </summary>
        public async Task<YoutubeDownloadTask> StartNewTaskAsync(
            string videoUrlOrId,
            List<StreamDto> streamsToDownload,
            string userId)
        {
            var safeVideoId = VideoId.Parse(videoUrlOrId).Value;
            var video = await _youtubeClient.Videos.GetAsync(safeVideoId);

            var chanId = video.Author.ChannelId.Value;
            var chanTitle = video.Author.ChannelTitle;
            var videoTitle = video.Title;

            var existChan = await _dbContext.YoutubeChannels.FindAsync(chanId);
            if (existChan == null)
            {
                _dbContext.YoutubeChannels.Add(new YoutubeChannel
                {
                    ChannelId = chanId,
                    ChannelTitle = chanTitle
                });
                await _dbContext.SaveChangesAsync();
            }

            var task = new YoutubeDownloadTask
            {
                Id = Guid.NewGuid().ToString(),
                VideoId = safeVideoId,
                ChannelId = chanId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Status = YoutubeWorkflowStatus.Created,
                Done = false,
                StreamsJson = JsonConvert.SerializeObject(streamsToDownload),
                Title = videoTitle
            };

            _dbContext.YoutubeDownloadTasks.Add(task);
            await _dbContext.SaveChangesAsync();
            return task;
        }

        /// <summary>
        /// Переходы статусов задачи.
        /// </summary>
        public async Task<YoutubeDownloadTask> ContinueDownloadAsync(string taskId)
        {
            var task = await _dbContext.YoutubeDownloadTasks
                .Include(t => t.Files)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null)
                throw new Exception($"Задача {taskId} не найдена.");

            if (task.Done || task.Status == YoutubeWorkflowStatus.Done)
                return task;

            try
            {
                switch (task.Status)
                {
                    case YoutubeWorkflowStatus.Created:
                        await RunDownloadAsync(task);
                        goto case YoutubeWorkflowStatus.Downloading;

                    case YoutubeWorkflowStatus.Downloading:
                        await RunMergingAsync(task);
                        goto case YoutubeWorkflowStatus.Merging;

                    case YoutubeWorkflowStatus.Merging:
                        task.Status = YoutubeWorkflowStatus.Done;
                        task.Done = true;
                        task.ModifiedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();
                        break;

                    case YoutubeWorkflowStatus.Error:
                        break;
                }

                return task;
            }
            catch (Exception ex)
            {
                task.Status = YoutubeWorkflowStatus.Error;
                task.Error = ex.Message;
                task.ModifiedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                throw;
            }
        }

        /// <summary>
        /// Шаг Downloading: скачать выбранные потоки (имена по Title).
        /// </summary>
        private async Task RunDownloadAsync(YoutubeDownloadTask task)
        {
            task.Status = YoutubeWorkflowStatus.Downloading;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var streams = JsonConvert.DeserializeObject<List<StreamDto>>(task.StreamsJson ?? "[]")
                          ?? new List<StreamDto>();

            Directory.CreateDirectory(_defaultSaveDir);

            var baseTitle = MakeSafeFileName(task.Title ?? task.VideoId);

            foreach (var s in streams)
            {
                var filename = $"{baseTitle}__{(s.Type ?? "audio")}_{(s.QualityLabel ?? "noQ")}_{(s.Language ?? "noLang")}.{s.Container ?? "mp4"}";
                var filePath = Path.Combine(_defaultSaveDir, filename);
                filePath = EnsureUniquePath(filePath);

                if (!File.Exists(filePath))
                {
                    await _youtubeStreamService.DownloadStreamAsync(
                        videoUrlOrId: task.VideoId,
                        type: s.Type ?? "audio",
                        qualityLabel: s.QualityLabel?.ToString(),
                        container: s.Container,
                        saveFilePath: filePath
                    );
                }

                _dbContext.YoutubeDownloadFiles.Add(new YoutubeDownloadFile
                {
                    TaskId = task.Id,
                    StreamType = s.Type,
                    QualityLabel = s.QualityLabel?.ToString(),
                    Container = s.Container,
                    Language = s.Language,
                    FilePath = filePath,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Шаг Merging: собрать выходной файл (имя по Title).
        /// </summary>
        private async Task RunMergingAsync(YoutubeDownloadTask task)
        {
            task.Status = YoutubeWorkflowStatus.Merging;
            task.ModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var files = await _dbContext.YoutubeDownloadFiles
                .Where(f => f.TaskId == task.Id)
                .ToListAsync();

            if (files.Count == 1)
            {
                task.MergedFilePath = files[0].FilePath;
                await _dbContext.SaveChangesAsync();
                return;
            }

            var videoFile = files.FirstOrDefault(f => f.StreamType == "video" || f.StreamType == "muxed");
            var audioFiles = files.Where(f => f.StreamType == "audio").ToList();

            if (videoFile == null)
            {
                if (audioFiles.Count > 0)
                {
                    task.MergedFilePath = audioFiles[0].FilePath;
                    await _dbContext.SaveChangesAsync();
                }
                return;
            }

            if (!audioFiles.Any())
            {
                task.MergedFilePath = videoFile.FilePath;
                await _dbContext.SaveChangesAsync();
                return;
            }

            if (videoFile.StreamType == "muxed")
            {
                task.MergedFilePath = videoFile.FilePath;
                await _dbContext.SaveChangesAsync();
                return;
            }

            var videoPath = videoFile.FilePath ?? throw new Exception("videoFile.FilePath is null.");
            var audioPaths = audioFiles
                .Select(a => a.FilePath!)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var baseTitle = MakeSafeFileName(task.Title ?? task.VideoId);
            var mergedName = $"{baseTitle}.mp4";
            var mergedPath = Path.Combine(_defaultSaveDir, mergedName);
            mergedPath = EnsureUniquePath(mergedPath);

            await _youtubeStreamService.MergeWithFFmpeg(videoPath, audioPaths, mergedPath);

            task.MergedFilePath = mergedPath;
            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Список задач пользователя (для таблицы).
        /// </summary>
        public async Task<List<MergedVideoDto>> GetMergedVideosAsync(string userId)
        {
            var tasks = await _dbContext.YoutubeDownloadTasks
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return tasks.Select(t =>
            {
                var prog = t.Status switch
                {
                    YoutubeWorkflowStatus.Created => 0,
                    YoutubeWorkflowStatus.Downloading => 50,
                    YoutubeWorkflowStatus.Merging => 90,
                    YoutubeWorkflowStatus.Done => 100,
                    _ => 0
                };

                var fileName = !string.IsNullOrWhiteSpace(t.MergedFilePath)
                    ? Path.GetFileName(t.MergedFilePath)
                    : null;

                var youtubeUrl = !string.IsNullOrWhiteSpace(t.VideoId)
                    ? $"https://youtu.be/{t.VideoId}"
                    : null;

                var downloadUrl = $"/api/youtube/tasks/{t.Id}/download";

                return new MergedVideoDto
                {
                    TaskId = t.Id,
                    FilePath = t.Status == YoutubeWorkflowStatus.Done ? t.MergedFilePath : null,
                    FileName = fileName,
                    Title = t.Title,
                    YoutubeId = t.VideoId,
                    YoutubeUrl = youtubeUrl,
                    DownloadUrl = t.Status == YoutubeWorkflowStatus.Done ? downloadUrl : null,
                    CreatedAt = t.CreatedAt,
                    Status = t.Status.ToString(),
                    Progress = prog
                };
            }).ToList();
        }

        /// <summary>
        /// DTO статуса для pollProgress.
        /// </summary>
        public async Task<TaskStatusDto> GetTaskStatusDtoAsync(string taskId)
        {
            var t = await _dbContext.YoutubeDownloadTasks.FirstOrDefaultAsync(x => x.Id == taskId)
                    ?? throw new Exception($"Задача {taskId} не найдена.");

            var dto = new TaskStatusDto
            {
                Status = t.Status.ToString(),
                Progress = t.Status switch
                {
                    YoutubeWorkflowStatus.Created => 0,
                    YoutubeWorkflowStatus.Downloading => 50,
                    YoutubeWorkflowStatus.Merging => 90,
                    YoutubeWorkflowStatus.Done => 100,
                    _ => 0
                },
                Error = t.Error
            };

            if (t.Status == YoutubeWorkflowStatus.Done && !string.IsNullOrWhiteSpace(t.MergedFilePath))
            {
                dto.FileName = Path.GetFileName(t.MergedFilePath);
                dto.DownloadUrl = $"/api/youtube/tasks/{t.Id}/download";
            }

            return dto;
        }

        public async Task<YoutubeDownloadTask?> GetTaskAsync(string taskId)
        {
            return await _dbContext.YoutubeDownloadTasks
                .Include(t => t.Files)
                .FirstOrDefaultAsync(t => t.Id == taskId);
        }

        // ===========================================================
        //                 SUBTITLES: JSON -> SRT / API
        // ===========================================================

        private sealed class CaptionPartJson
        {
            public string? Text { get; set; }
            public string? Offset { get; set; }
        }

        private sealed class CaptionItemJson
        {
            public string? Text { get; set; }
            public string? Offset { get; set; }
            public string? Duration { get; set; }
            public List<CaptionPartJson>? Parts { get; set; }
        }

        private sealed class ParsedCaption
        {
            public TimeSpan Start { get; set; }
            public TimeSpan? End { get; set; }
            public string Text { get; set; } = "";
        }

        /// <summary>
        /// Отдаёт SRT по taskId. Порядок:
        /// 1) Если есть сохранённый .srt файл -> вернуть его.
        /// 2) Если есть .json субтитры -> конвертировать в SRT (и опц. сохранить).
        /// 3) Иначе — забрать дорожку через YouTubeExplode (предпочитая lang и не-авто), сконвертировать в SRT (и опц. сохранить).
        /// </summary>
        public async Task<(byte[] Content, string FileName)> GetSubtitlesSrtAsync(
      string taskId,
      string? lang = null,
      bool persistFile = true)
        {
            var task = await _dbContext.YoutubeDownloadTasks
                .Include(t => t.Files)
                .FirstOrDefaultAsync(t => t.Id == taskId)
                ?? throw new Exception($"Задача {taskId} не найдена.");

            var baseTitle = MakeSafeFileName(task.Title ?? task.VideoId);
            string BuildFileName(string? code) =>
                !string.IsNullOrWhiteSpace(code) ? $"{baseTitle}.{code}.srt" : $"{baseTitle}.srt";

            // 0) Пробуем взять JSON субтитров из таблицы YoutubeCaptionTexts (Caption = JSON)
            var dbCaption = await _dbContext.YoutubeCaptionTexts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == taskId);

            if (dbCaption != null && !string.IsNullOrWhiteSpace(dbCaption.Caption))
            {
                var srt = ConvertJsonToSrt(dbCaption.Caption);
                var fileNameDb = BuildFileName(lang);

                if (persistFile)
                {
                    Directory.CreateDirectory(_defaultSaveDir);
                    var outPath = EnsureUniquePath(Path.Combine(_defaultSaveDir, fileNameDb));
                    await File.WriteAllTextAsync(outPath, srt, Encoding.UTF8);

                    _dbContext.YoutubeDownloadFiles.Add(new YoutubeDownloadFile
                    {
                        TaskId = task.Id,
                        StreamType = "subtitles",
                        Container = "srt",
                        Language = string.IsNullOrWhiteSpace(lang) ? null : lang,
                        FilePath = outPath,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync();

                    return (await File.ReadAllBytesAsync(outPath), Path.GetFileName(outPath));
                }

                return (Encoding.UTF8.GetBytes(srt), fileNameDb);
            }

            // 1) Уже есть .srt на диске?
            var srtFile = task.Files?
                .FirstOrDefault(f =>
                    (f.StreamType == "subtitles" || f.StreamType == "captions") &&
                    !string.IsNullOrWhiteSpace(f.FilePath) &&
                    string.Equals(Path.GetExtension(f.FilePath), ".srt", StringComparison.OrdinalIgnoreCase) &&
                    (lang == null || string.Equals(f.Language, lang, StringComparison.OrdinalIgnoreCase)));

            if (srtFile?.FilePath != null && File.Exists(srtFile.FilePath))
            {
                var bytes = await File.ReadAllBytesAsync(srtFile.FilePath);
                return (bytes, Path.GetFileName(srtFile.FilePath));
            }

            // 2) Есть .json субтитры на диске?
            var jsonFile = task.Files?
                .FirstOrDefault(f =>
                    (f.StreamType == "subtitles" || f.StreamType == "captions") &&
                    !string.IsNullOrWhiteSpace(f.FilePath) &&
                    string.Equals(Path.GetExtension(f.FilePath), ".json", StringComparison.OrdinalIgnoreCase) &&
                    (lang == null || string.Equals(f.Language, lang, StringComparison.OrdinalIgnoreCase)));

            if (jsonFile?.FilePath != null && File.Exists(jsonFile.FilePath))
            {
                var json = await File.ReadAllTextAsync(jsonFile.FilePath, Encoding.UTF8);
                var srt = ConvertJsonToSrt(json);
                var langCode = !string.IsNullOrWhiteSpace(lang) ? lang : jsonFile.Language;
                var fileName = BuildFileName(langCode);

                if (persistFile)
                {
                    Directory.CreateDirectory(_defaultSaveDir);
                    var outPath = EnsureUniquePath(Path.Combine(_defaultSaveDir, fileName));
                    await File.WriteAllTextAsync(outPath, srt, Encoding.UTF8);

                    _dbContext.YoutubeDownloadFiles.Add(new YoutubeDownloadFile
                    {
                        TaskId = task.Id,
                        StreamType = "subtitles",
                        Container = "srt",
                        Language = langCode,
                        FilePath = outPath,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync();

                    return (await File.ReadAllBytesAsync(outPath), Path.GetFileName(outPath));
                }

                return (Encoding.UTF8.GetBytes(srt), fileName);
            }

            // 3) Фолбэк — тянем дорожку через YouTubeExplode
            var manifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(task.VideoId);
            var tracks = manifest.Tracks;

            var trackInfo =
                (lang != null
                    ? tracks
                        .Where(t => string.Equals(t.Language.Code, lang, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(t => t.IsAutoGenerated) // не-авто приоритет
                        .FirstOrDefault()
                    : null)
                ?? tracks.Where(t => !t.IsAutoGenerated).FirstOrDefault()
                ?? tracks.FirstOrDefault();

            if (trackInfo == null)
                throw new Exception("Для этого видео субтитры не найдены.");

            var track = await _youtubeClient.Videos.ClosedCaptions.GetAsync(trackInfo);
            var srtFromTrack = BuildSrtFromTrack(track.Captions);

            var langFromTrack = !string.IsNullOrWhiteSpace(lang) ? lang : trackInfo.Language.Code;
            var outName = BuildFileName(langFromTrack);

            if (persistFile)
            {
                Directory.CreateDirectory(_defaultSaveDir);
                var outPath = EnsureUniquePath(Path.Combine(_defaultSaveDir, outName));
                await File.WriteAllTextAsync(outPath, srtFromTrack, Encoding.UTF8);

                _dbContext.YoutubeDownloadFiles.Add(new YoutubeDownloadFile
                {
                    TaskId = task.Id,
                    StreamType = "subtitles",
                    Container = "srt",
                    Language = langFromTrack,
                    FilePath = outPath,
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();

                return (await File.ReadAllBytesAsync(outPath), Path.GetFileName(outPath));
            }

            return (Encoding.UTF8.GetBytes(srtFromTrack), outName);
        }


        private static string ConvertJsonToSrt(string json)
        {
            var items = JsonConvert.DeserializeObject<List<CaptionItemJson>>(json) ?? new();
            return BuildSrtFromJson(items);
        }

        private static string BuildSrtFromJson(IEnumerable<CaptionItemJson> items)
        {
            var list = items.Select(i => new ParsedCaption
            {
                Start = ParseTs(i.Offset),
                End = string.IsNullOrWhiteSpace(i.Duration) ? null : ParseTs(i.Offset) + ParseTs(i.Duration),
                Text = SanitizeText(i.Text, i.Parts)
            })
            .OrderBy(x => x.Start)
            .ToList();

            var sb = new StringBuilder();
            int idx = 1;

            for (int i = 0; i < list.Count; i++)
            {
                var cur = list[i];
                if (string.IsNullOrWhiteSpace(cur.Text) || cur.Text.Trim() == "\\n" || cur.Text.Trim() == "\n")
                    continue;

                var start = cur.Start;
                var end = cur.End ?? FindNextStart(list, i) - TimeSpan.FromMilliseconds(1);
                if (end <= start) end = start + TimeSpan.FromMilliseconds(500);

                sb.AppendLine(idx.ToString());
                sb.AppendLine($"{Fmt(start)} --> {Fmt(end)}");
                sb.AppendLine(cur.Text);
                sb.AppendLine();
                idx++;
            }

            return sb.ToString();

            static TimeSpan ParseTs(string? s) => string.IsNullOrWhiteSpace(s) ? TimeSpan.Zero : TimeSpan.Parse(s);

            static TimeSpan FindNextStart(List<ParsedCaption> list, int i)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var t = list[j].Text;
                    if (!string.IsNullOrWhiteSpace(t) && t.Trim() != "\\n" && t.Trim() != "\n")
                        return list[j].Start;
                }
                return list[i].Start + TimeSpan.FromSeconds(2);
            }
        }

        private static string BuildSrtFromTrack(IEnumerable<YoutubeExplode.Videos.ClosedCaptions.ClosedCaption> captions)
        {
            var ordered = captions.OrderBy(c => c.Offset).ToList();

            var sb = new StringBuilder();
            int idx = 1;

            for (int i = 0; i < ordered.Count; i++)
            {
                var c = ordered[i];
                var text = SanitizeTrackText(c);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var start = c.Offset;

                // Duration в вашей версии — НЕ nullable.
                TimeSpan end;
                if (c.Duration > TimeSpan.Zero)
                {
                    end = c.Offset + c.Duration;
                }
                else if (i + 1 < ordered.Count)
                {
                    end = ordered[i + 1].Offset - TimeSpan.FromMilliseconds(1);
                }
                else
                {
                    end = start + TimeSpan.FromSeconds(2);
                }

                if (end <= start)
                    end = start + TimeSpan.FromMilliseconds(500);

                sb.AppendLine(idx.ToString());
                sb.AppendLine($"{Fmt(start)} --> {Fmt(end)}");
                sb.AppendLine(text);
                sb.AppendLine();
                idx++;
            }

            return sb.ToString();

            static string SanitizeTrackText(YoutubeExplode.Videos.ClosedCaptions.ClosedCaption c)
            {
                var t = c.Text;
                if (string.IsNullOrWhiteSpace(t) && c.Parts?.Any() == true)
                    t = string.Concat(c.Parts.Select(p => p.Text));

                return (t ?? "").Replace("\r", "").Trim();
            }
        }

        private static string SanitizeText(string? text, List<CaptionPartJson>? parts)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "\\n" || text.Trim() == "\n")
            {
                if (parts != null && parts.Count > 0)
                    return string.Concat(parts.Select(p => p.Text ?? "")).Replace("\r", "").Trim();
                return "";
            }
            return text.Replace("\r", "").Trim();
        }

        private static string Fmt(TimeSpan t)
            => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";

        // ----------------- Helpers -----------------

        private static string MakeSafeFileName(string? s, int maxLen = 120)
        {
            if (string.IsNullOrWhiteSpace(s)) return "untitled";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                sb.Append(invalid.Contains(ch) ? '_' : ch);

            var cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
            };
            if (reserved.Contains(cleaned)) cleaned = "_" + cleaned;

            if (cleaned.Length > maxLen) cleaned = cleaned[..maxLen];
            cleaned = cleaned.Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
        }

        private static string EnsureUniquePath(string path)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            var candidate = path;
            int i = 1;
            while (File.Exists(candidate))
                candidate = Path.Combine(dir, $"{name} ({i++}){ext}");

            return candidate;
        }
    }
}
