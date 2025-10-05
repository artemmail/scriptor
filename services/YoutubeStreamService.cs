using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using Xabe.FFmpeg;
using YoutubeDownload.Models;    // StreamDto, YoutubeStreamCache
using YandexSpeech;
using YandexSpeech.models.DB;             // MyDbContext
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    /// <summary>
    /// DTO для информации о потоке YouTube
    /// </summary>
    public class StreamDto
    {
        public string Type { get; set; } = null!;      // audio / video / muxed
        public string? Codec { get; set; }
        public long? Bitrate { get; set; }
        public long? Size { get; set; }
        public string? Container { get; set; }
        public string? Language { get; set; }
        public string? QualityLabel { get; set; }
    }

    /// <summary>
    /// Сервис для получения, кеширования и скачивания потоков с YouTube, а также склейки дорожек.
    /// </summary>
    public class YoutubeStreamService
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly MyDbContext _dbContext;
        private readonly IFfmpegService _ffmpegService;
        private readonly string _defaultSaveDir;

        public YoutubeStreamService(
            IConfiguration configuration,
            MyDbContext dbContext,
            IFfmpegService ffmpegService)
        {
            _defaultSaveDir = configuration.GetValue<string>("SaveDirectory")
                                ?? @"C:\Temp";
            _youtubeClient = new YoutubeClient();
            _dbContext = dbContext;
            _ffmpegService = ffmpegService;
        }

        /// <summary>
        /// Возвращает все потоки для видео. Результат кешируется в БД (таблица YoutubeStreamCaches).
        /// </summary>
        public async Task<List<StreamDto>> GetAllStreamsAsync(string videoUrlOrId)
        {
            var vid = VideoId.Parse(videoUrlOrId).Value;
            // Проверяем кеш
            var cache = await _dbContext.YoutubeStreamCaches.FindAsync(vid);
            if (cache != null)
                return JsonConvert.DeserializeObject<List<StreamDto>>(cache.StreamsJson)!;

            // Получаем манифест
            var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(vid);
            var list = new List<StreamDto>();

            // 1) Audio-only
            foreach (var a in manifest.GetAudioOnlyStreams())
            {
                list.Add(new StreamDto
                {
                    Type = "audio",
                    Codec = a.AudioCodec,
                    Bitrate = a.Bitrate.BitsPerSecond,
                    Size = a.Size.Bytes,
                    Container = a.Container.Name,
                    Language = a.AudioLanguage?.Name,
                    QualityLabel = null
                });
            }

            // 2) Video-only
            foreach (var v in manifest.GetVideoOnlyStreams())
            {
                list.Add(new StreamDto
                {
                    Type = "video",
                    Codec = v.VideoCodec,
                    Bitrate = v.Bitrate.BitsPerSecond,
                    Size = v.Size.Bytes,
                    Container = v.Container.Name,
                    Language = null,
                    QualityLabel = v.VideoQuality.Label
                });
            }

            // 3) Muxed (deprecated, часто пусто, но тип есть)
            foreach (var m in manifest.GetMuxedStreams())
            {
                list.Add(new StreamDto
                {
                    Type = "muxed",
                    Codec = m.Container.Name,
                    Bitrate = m.Bitrate.BitsPerSecond,
                    Size = m.Size.Bytes,
                    Container = m.Container.Name,
                    Language = null,
                    QualityLabel = m.VideoQuality.Label
                });
            }

            // Сохраняем в БД
            var json = JsonConvert.SerializeObject(list);
            _dbContext.YoutubeStreamCaches.Add(new YoutubeStreamCache
            {
                VideoId = vid,
                StreamsJson = json,
                RetrievedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            return list;
        }

        /// <summary>
        /// Скачивает один поток (audio, video или muxed) из YouTube в файл.
        /// </summary>
        /// <param name="videoUrlOrId">ID или URL видео</param>
        /// <param name="type">"audio", "video" или "muxed"</param>
        /// <param name="qualityLabel">например "720p", null — без фильтра</param>
        /// <param name="container">например "mp4", null — без фильтра</param>
        /// <param name="saveFilePath">путь, куда сохранять</param>
        public async Task DownloadStreamAsync(
            string videoUrlOrId,
            string type,
            string? qualityLabel,
            string? container,
            string saveFilePath)
        {
            // директория
            var dir = Path.GetDirectoryName(saveFilePath)
                      ?? _defaultSaveDir;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // получаем манифест
            var vid = VideoId.Parse(videoUrlOrId);
            var manifest = await _youtubeClient.Videos.Streams
                                .GetManifestAsync(vid);

            IStreamInfo? chosen = type.ToLowerInvariant() switch
            {
                "audio" => manifest.GetAudioOnlyStreams()
                                   .Where(s => qualityLabel == null
                                        || string.Equals(s.AudioLanguage?.Name, qualityLabel, StringComparison.OrdinalIgnoreCase))
                                   .FirstOrDefault(),

                "video" => manifest.GetVideoOnlyStreams()
                                   .Where(s => (qualityLabel == null
                                            || s.VideoQuality.Label.Equals(qualityLabel, StringComparison.OrdinalIgnoreCase))
                                            && (container == null
                                            || s.Container.Name.Equals(container, StringComparison.OrdinalIgnoreCase)))
                                   .FirstOrDefault(),

                "muxed" => manifest.GetMuxedStreams()
                                   .Where(s => (qualityLabel == null
                                            || s.VideoQuality.Label.Equals(qualityLabel, StringComparison.OrdinalIgnoreCase))
                                            && (container == null
                                            || s.Container.Name.Equals(container, StringComparison.OrdinalIgnoreCase)))
                                   .FirstOrDefault(),

                _ => throw new ArgumentException("Type must be audio, video or muxed")
            };

            if (chosen == null)
                throw new InvalidOperationException(
                    $"Поток {type} {(qualityLabel != null ? qualityLabel : "")}" +
                    $"{(container != null ? $" / {container}" : "")} не найден.");

            await _youtubeClient.Videos.Streams
                .DownloadAsync(chosen, saveFilePath);
        }

        /// <summary>
        /// Сливает одну видеодорожку с аудиодорожками через FFmpeg.
        /// </summary>
        /// <param name="videoPath">путь к видеофайлу</param>
        /// <param name="audioPaths">пути к аудиофайлам</param>
        /// <param name="outputPath">куда сохранять результат</param>
        public async Task MergeWithFFmpeg(
            string videoPath,
            List<string> audioPaths,
            string outputPath)
        {
            // директория
            var outDir = Path.GetDirectoryName(outputPath)
                         ?? _defaultSaveDir;
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var ffmpegDirectory = _ffmpegService.ResolveFfmpegDirectory();
            if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            {
                FFmpeg.SetExecutablesPath(ffmpegDirectory);
            }
            var conversion = FFmpeg.Conversions.New();

            // видео
            var vInfo = await FFmpeg.GetMediaInfo(videoPath);
            var vStream = vInfo.VideoStreams.FirstOrDefault()
                          ?? throw new InvalidOperationException("Видеопоток не найден");
            conversion.AddStream(vStream);

            // аудио
            foreach (var aPath in audioPaths)
            {
                var aInfo = await FFmpeg.GetMediaInfo(aPath);
                var aStream = aInfo.AudioStreams.FirstOrDefault();
                if (aStream != null)
                    conversion.AddStream(aStream);
            }

            conversion.SetOutput(outputPath);
            await conversion.Start();
        }
    }
}
