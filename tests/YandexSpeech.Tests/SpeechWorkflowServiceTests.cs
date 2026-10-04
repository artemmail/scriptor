using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech.models.DB;
using YandexSpeech.services;

namespace YandexSpeech.Tests;

public class SpeechWorkflowServiceTests
{
    [Fact]
    public async Task RecognitionPersistsTransitionsAndResult()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var db = CreateDb();
            db.AudioFiles.Add(new AudioFile
            {
                Id = "file-a", OriginalFileName = "sample.mp3",
                OriginalFilePath = path, CreatedBy = "alice"
            });
            await db.SaveChangesAsync();

            var files = new FakeAudioFiles(db, path);
            var speech = new FakeSpeech();
            var workflow = CreateWorkflow(db, files, speech);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                workflow.StartRecognitionTaskAsync("file-a", "bob"));

            var task = await workflow.StartRecognitionTaskAsync("file-a", "alice");
            Assert.Equal(task.Id, (await workflow.StartRecognitionTaskAsync("file-a", "alice")).Id);

            await workflow.ContinueRecognitionAsync(task.Id);
            Assert.Equal(RecognizeStatus.Uploading, task.Status);
            await workflow.ContinueRecognitionAsync(task.Id);
            Assert.Equal(RecognizeStatus.Recognizing, task.Status);
            Assert.Equal(task.ObjectKey, speech.ObjectKey);
            await workflow.ContinueRecognitionAsync(task.Id);
            Assert.Equal(RecognizeStatus.Recognizing, task.Status);
            await workflow.ContinueRecognitionAsync(task.Id);

            Assert.True(task.Done);
            Assert.Equal(RecognizeStatus.Done, task.Status);
            Assert.Equal("Первый фрагмент. Второй фрагмент.", task.Result);
            Assert.Equal(2, task.SegmentsProcessed);
            Assert.Equal(2, await db.AudioWorkflowSegments.CountAsync());
            await workflow.ContinueRecognitionAsync(task.Id);
            Assert.Equal(2, await db.AudioWorkflowSegments.CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SpeechApiFailureMarksTaskAsErrorAndAllowsRetry()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var db = CreateDb();
            db.AudioFiles.Add(new AudioFile
            {
                Id = "file-b", OriginalFileName = "sample.mp3",
                OriginalFilePath = path, CreatedBy = "alice",
                ConvertedFilePath = path
            });
            await db.SaveChangesAsync();
            var speech = new FakeSpeech { Fail = true };
            var workflow = CreateWorkflow(db, new FakeAudioFiles(db, path), speech);
            var task = await workflow.StartRecognitionTaskAsync("file-b", "alice");
            await workflow.ContinueRecognitionAsync(task.Id);
            await workflow.ContinueRecognitionAsync(task.Id);
            await workflow.ContinueRecognitionAsync(task.Id);
            await workflow.ContinueRecognitionAsync(task.Id);

            Assert.Equal(RecognizeStatus.Error, task.Status);
            Assert.True(task.Done);
            Assert.Contains("Не удалось распознать аудио", task.Error);
            Assert.NotEqual(task.Id, (await workflow.StartRecognitionTaskAsync("file-b", "alice")).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SpeechWorkflowService CreateWorkflow(
        MyDbContext db, IAudioFileService files, IYSpeechService speech) => new(
            db, files, speech,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["YSpeech:DefaultBucketName"] = "test-bucket"
            }).Build(),
            NullLogger<SpeechWorkflowService>.Instance);

    private sealed class FakeAudioFiles(MyDbContext db, string path) : IAudioFileService
    {
        public async Task<AudioFile> ConvertToOpusAsync(string id)
        {
            var file = (await db.AudioFiles.FindAsync(id))!;
            file.ConvertedFilePath = path;
            await db.SaveChangesAsync();
            return file;
        }

        public Task<AudioFile> SaveOriginalAsync(Stream stream, string name, string owner)
            => throw new NotImplementedException();
        public Task<IEnumerable<AudioFile>> GetAllAsync()
            => throw new NotImplementedException();
        public Task<AudioFile?> GetByIdAsync(string id)
            => throw new NotImplementedException();
    }

    private sealed class FakeSpeech : IYSpeechService
    {
        public string? ObjectKey { get; private set; }
        public bool Fail { get; set; }
        private int _polls;

        public Task<RecognizeResult> UploadOpusAndRecognizeAsync(
            string path, string bucket = null!, string key = null!)
        {
            ObjectKey = key;
            return Task.FromResult(new RecognizeResult { id = "operation-a" });
        }

        public Task<RecognizeResult> getRes(string id)
        {
            _polls++;
            if (_polls == 1)
                return Task.FromResult(new RecognizeResult { done = false });
            if (Fail)
                return Task.FromResult(new RecognizeResult
                {
                    done = true,
                    error = new RecognizeOperationError { code = 400, message = "bad audio" }
                });
            return Task.FromResult(new RecognizeResult
            {
                done = true,
                response = new RecognizeResponse
                {
                    chunks =
                    [
                        new Chunk { alternatives = [new Alternative { text = "Первый фрагмент." }] },
                        new Chunk { alternatives = [new Alternative { text = "Второй фрагмент." }] }
                    ]
                }
            });
        }

        public Task<string> GetToken() => throw new NotImplementedException();
        public Task<Tokens> GetKeys(string token) => throw new NotImplementedException();
        public Task<GPTResult> GptAsk(string request) => throw new NotImplementedException();
        public Task<RecognizeResult> Recognize(string uri) => throw new NotImplementedException();
        public Task Speech(string token, string text, string filename) => throw new NotImplementedException();
        public Task<List<S3Bucket>> ListBucketsAsync(string key, string secret)
            => throw new NotImplementedException();
        public Task UploadFileToBucketAsync(string path, string bucket, string key)
            => throw new NotImplementedException();
        public Task ConvertMp3ToOpusAsync(string input, string output)
            => throw new NotImplementedException();
        public Task<string> ConvertMp3UploadAndRecognizeAsync(string input)
            => throw new NotImplementedException();
    }
}
