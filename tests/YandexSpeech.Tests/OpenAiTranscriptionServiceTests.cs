using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Whisper;

namespace YandexSpeech.Tests;

public sealed class OpenAiTranscriptionServiceTests
{
    [Fact]
    public async Task ContinueTranscriptionAsync_ProcessesAllSegmentsUntilDone()
    {
        var dbOptions = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new MyDbContext(dbOptions);

        var task = new OpenAiTranscriptionTask
        {
            SourceFilePath = "test.wav",
            CreatedBy = "tester",
            Status = OpenAiTranscriptionStatus.ProcessingSegments,
            SegmentsTotal = 10,
            SegmentsProcessed = 0,
            Done = false,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        dbContext.OpenAiTranscriptionTasks.Add(task);

        var segments = Enumerable.Range(0, task.SegmentsTotal)
            .Select(i => new OpenAiRecognizedSegment
            {
                TaskId = task.Id,
                Task = task,
                Order = i,
                Text = $"segment-{i}",
                IsProcessed = false,
                IsProcessing = false
            })
            .ToList();

        dbContext.OpenAiRecognizedSegments.AddRange(segments);
        await dbContext.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FfmpegExePath"] = "/usr/bin/ffmpeg",
                ["OpenAI:ApiKey"] = "fake-key"
            })
            .Build();

        var punctuation = new StubPunctuationService();
        var whisper = new StubWhisperTranscriptionService();
        var ffmpeg = new StubFfmpegService();

        var service = new TestOpenAiTranscriptionService(
            dbContext,
            configuration,
            NullLogger<OpenAiTranscriptionService>.Instance,
            punctuation,
            whisper,
            new StubHttpClientFactory(),
            new StubYandexDiskDownloadService(),
            ffmpeg);

        var result = await service.ContinueTranscriptionAsync(task.Id);

        Assert.NotNull(result);
        Assert.True(result!.Done);
        Assert.Equal(OpenAiTranscriptionStatus.Done, result.Status);
        Assert.Equal(task.SegmentsTotal, result.SegmentsProcessed);
        Assert.NotNull(result.MarkdownText);
        Assert.Contains("segment-", result.ProcessedText);

        var persistedTask = await dbContext.OpenAiTranscriptionTasks
            .Include(t => t.Segments)
            .Include(t => t.Steps)
            .FirstAsync(t => t.Id == task.Id);

        Assert.True(persistedTask.Segments.All(s => s.IsProcessed));
        Assert.Equal(task.SegmentsTotal, persistedTask.SegmentsProcessed);
        Assert.Equal(OpenAiTranscriptionStatus.Done, persistedTask.Status);
        Assert.True(persistedTask.Done);

        Assert.Contains(persistedTask.Steps, s =>
            s.Step == OpenAiTranscriptionStatus.ProcessingSegments &&
            s.Status == OpenAiTranscriptionStepStatus.Completed);
        Assert.Contains(persistedTask.Steps, s =>
            s.Step == OpenAiTranscriptionStatus.Formatting &&
            s.Status == OpenAiTranscriptionStepStatus.Completed);
    }

    private sealed class StubPunctuationService : IPunctuationService
    {
        public Task<string> GetAvailableModelsAsync() => Task.FromResult("stub");

        public Task<string> FixPunctuationAsync(
            string rawText,
            string? previousContext,
            string profileName,
            string? clarification = null)
            => Task.FromResult($"{rawText}-processed");
    }

    private sealed class StubWhisperTranscriptionService : IWhisperTranscriptionService
    {
        public Task<WhisperTranscriptionResult> TranscribeAsync(
            string audioFilePath,
            string workingDirectory,
            string? ffmpegExecutable,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WhisperTranscriptionResult
            {
                RawJson = "{}",
                TimecodedText = string.Empty
            });
        }
    }

    private sealed class TestOpenAiTranscriptionService : OpenAiTranscriptionService
    {
        public TestOpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            Microsoft.Extensions.Logging.ILogger<OpenAiTranscriptionService> logger,
            IPunctuationService punctuationService,
            IWhisperTranscriptionService whisperTranscriptionService,
            IHttpClientFactory httpClientFactory,
            IYandexDiskDownloadService yandexDiskDownloadService,
            IFfmpegService ffmpegService)
            : base(dbContext, configuration, logger, punctuationService, whisperTranscriptionService, httpClientFactory, yandexDiskDownloadService, ffmpegService)
        {
        }

        protected override Task<string> CreateDialogueMarkdownAsync(string transcription, string? clarification)
        {
            return Task.FromResult($"markdown::{transcription}");
        }
    }

    private sealed class StubFfmpegService : IFfmpegService
    {
        public Task ConvertToWav16kMonoAsync(
            string sourcePath,
            string outputPath,
            CancellationToken cancellationToken = default,
            string? overrideExecutable = null)
        {
            if (!File.Exists(outputPath))
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, string.Empty);
            }

            return Task.CompletedTask;
        }

        public string ResolveFfmpegExecutable(string? overrideExecutable = null) => "/usr/bin/ffmpeg";

        public string? ResolveFfmpegDirectory(string? overrideExecutable = null) => "/usr/bin";
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new HttpClientHandler());
        }
    }

    private sealed class StubYandexDiskDownloadService : IYandexDiskDownloadService
    {
        public bool IsYandexDiskUrl(Uri uri) => false;

        public Task<YandexDiskDownloadResult> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new YandexDiskDownloadResult(false, null, null, "Not supported"));
        }
    }
}
