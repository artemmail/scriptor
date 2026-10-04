using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YourNamespace.Controllers;
using YoutubeDownload.Managers;
using YoutubeDownload.Models;
using YoutubeDownload.Services;

namespace YandexSpeech.Tests;

public class ServerRefactorTests
{
    [Fact]
    public async Task YoutubeTaskEndpointsDoNotExposeAnotherUsersTask()
    {
        var task = new YoutubeDownloadTask
        {
            Id = "task-1",
            VideoId = "dQw4w9WgXcQ",
            UserId = "alice",
            Status = YoutubeWorkflowStatus.Done,
            Done = true,
            MergedFilePath = "does-not-need-to-exist.mp4"
        };
        var controller = CreateYoutubeController(new DownloadTaskManagerStub(task), "bob");

        Assert.IsType<NotFoundResult>(await controller.GetProgress(task.Id));
        Assert.IsType<NotFoundResult>(await controller.DownloadMergedResult(task.Id));
    }

    [Fact]
    public async Task YoutubeTaskEndpointsReturnNotFoundForUnknownTask()
    {
        var controller = CreateYoutubeController(new DownloadTaskManagerStub(null), "alice");

        Assert.IsType<NotFoundResult>(await controller.GetProgress("missing"));
        Assert.IsType<NotFoundResult>(await controller.DownloadMergedResult("missing"));
    }

    [Fact]
    public async Task YoutubeTaskManagerQueriesOnlyTheOwnersTask()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<MyDbContext>(options => options.UseInMemoryDatabase(name));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            db.YoutubeDownloadTasks.Add(new YoutubeDownloadTask
            {
                Id = "owned-task", VideoId = "dQw4w9WgXcQ", UserId = "alice"
            });
            await db.SaveChangesAsync();
        }

        var manager = new YoutubeDownloadTaskManager(provider.GetRequiredService<IServiceScopeFactory>());
        Assert.Null(await manager.GetTaskStatusAsync("owned-task", "bob"));
        Assert.NotNull(await manager.GetTaskStatusAsync("owned-task", "alice"));
    }

    [Fact]
    public async Task YoutubeResultStreamsOwnedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scriptor-test-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        try
        {
            var task = new YoutubeDownloadTask
            {
                Id = "task-2",
                VideoId = "dQw4w9WgXcQ",
                UserId = "alice",
                Status = YoutubeWorkflowStatus.Done,
                Done = true,
                MergedFilePath = path
            };
            var controller = CreateYoutubeController(new DownloadTaskManagerStub(task), "alice");

            var result = Assert.IsType<PhysicalFileResult>(await controller.DownloadMergedResult(task.Id));
            Assert.Equal(path, result.FileName);
            Assert.Equal("video/mp4", result.ContentType);
            var progress = Assert.IsType<OkObjectResult>(await controller.GetProgress(task.Id));
            Assert.Contains("/api/youtube/downloadResult/task-2", JsonSerializer.Serialize(progress.Value));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task YoutubeDownloadRejectsUnsafeFileExtensionAndInvalidVideoId()
    {
        var controller = CreateYoutubeController(new DownloadTaskManagerStub(null), "alice");

        Assert.IsType<BadRequestObjectResult>(await controller.DownloadStream(
            "dQw4w9WgXcQ", "audio", null, "../outside"));
        Assert.IsType<BadRequestObjectResult>((await controller.GetAllStreams("invalid video id")).Result);
    }

    [Fact]
    public async Task AudioTasksAreFilteredAndDeletedByOwner()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<MyDbContext>(options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            db.AudioFiles.AddRange(
                new AudioFile { Id = "file-a", OriginalFileName = "a.mp3", OriginalFilePath = "a", CreatedBy = "alice" },
                new AudioFile { Id = "file-b", OriginalFileName = "b.mp3", OriginalFilePath = "b", CreatedBy = "bob" });
            db.AudioWorkflowTasks.AddRange(
                new AudioWorkflowTask { Id = "task-a", AudioFileId = "file-a", CreatedBy = "alice", BucketName = "test" },
                new AudioWorkflowTask { Id = "task-b", AudioFileId = "file-b", CreatedBy = "bob", BucketName = "test" });
            await db.SaveChangesAsync();
        }

        var manager = new AudioTaskManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AudioTaskManager>.Instance);

        Assert.Null(await manager.GetTaskStatusAsync("task-b", "alice"));
        Assert.Equal("task-a", Assert.Single(await manager.GetAllTasksAsync("alice")).Id);
        Assert.False(await manager.DeleteTaskAsync("task-b", "alice"));
        Assert.True(await manager.DeleteTaskAsync("task-a", "alice"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.EnqueueRecognitionTaskAsync("file-b", "alice"));
        await Assert.ThrowsAsync<AudioWorkflowUnavailableException>(() =>
            manager.EnqueueRecognitionTaskAsync("file-a", "alice"));
        var controller = new AudioWorkflowController(manager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "alice")], "Test"))
                }
            }
        };
        var unavailable = Assert.IsType<ObjectResult>((await controller.StartRecognition("file-a")).Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
    }

    [Fact]
    public async Task YoutubeHistoryUsesExistingDownloadRouteWithoutExposingServerPath()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new MyDbContext(options);
        db.YoutubeDownloadTasks.Add(new YoutubeDownloadTask
        {
            Id = "task-3",
            VideoId = "dQw4w9WgXcQ",
            UserId = "alice",
            Status = YoutubeWorkflowStatus.Done,
            Done = true,
            MergedFilePath = "C:\\Temp\\private.mp4"
        });
        await db.SaveChangesAsync();

        var workflow = new YoutubeWorkflowService(db, null!, null!);
        var item = Assert.Single(await workflow.GetMergedVideosAsync("alice"));
        Assert.Equal("/api/youtube/downloadResult/task-3", item.DownloadUrl);
        Assert.Null(item.FilePath);
    }

    [Fact]
    public void DocumentOutputPathsAreUniqueAndStayInTemporaryDirectory()
    {
        var first = DocumentGeneratorService.CreateTemporaryOutputPath("pdf");
        var second = DocumentGeneratorService.CreateTemporaryOutputPath("pdf");

        Assert.NotEqual(first, second);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(first));
        Assert.EndsWith(".pdf", first);
    }

    [Fact]
    public async Task DocumentControllerSanitizesDownloadNameAndRejectsEmptyBody()
    {
        var controller = new GenerateController(new DocumentGeneratorStub());

        Assert.IsType<BadRequestObjectResult>(await controller.GenerateBbcode(null));
        var result = Assert.IsType<FileContentResult>(await controller.GenerateBbcode(
            new GenerateRequest { Id = "../report\r\n.txt", Markdown = "sample" }));
        Assert.Equal("reporttxt.bbcode", result.FileDownloadName);
    }

    private static YoutubeController CreateYoutubeController(IYoutubeDownloadTaskManager manager, string userId)
    {
        return new YoutubeController(null!, manager, null!, NullLogger<YoutubeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
                }
            }
        };
    }

    private sealed class DownloadTaskManagerStub(YoutubeDownloadTask? task) : IYoutubeDownloadTaskManager
    {
        public Task<string> EnqueueDownloadAsync(string videoId, List<YandexSpeech.services.StreamDto> streamsToDownload, string createdBy)
            => throw new NotImplementedException();

        public Task<YoutubeDownloadTask?> GetTaskStatusAsync(string taskId, string userId)
            => Task.FromResult(task?.Id == taskId && task.UserId == userId ? task : null);

        public void ProcessQueue() => throw new NotImplementedException();
        public Task ResumeIncompleteTasksAsync() => throw new NotImplementedException();
    }

    private sealed class DocumentGeneratorStub : IDocumentGeneratorService
    {
        public Task<string> GeneratePdfFromMarkdownAsync(string id, string markdown) => throw new NotImplementedException();
        public Task<string> GenerateWordFromMarkdownAsync(string id, string markdown) => throw new NotImplementedException();
        public Task<string> GenerateBbcodeFromMarkdownAsync(string id, string markdown) => Task.FromResult("bbcode");
        public Task<string> GenerateSrtFromDbJsonAsync(string taskId, string? lang = null) => throw new NotImplementedException();
        public Task<string> GenerateMarkdownFromHtmlAsync(string id, string html) => throw new NotImplementedException();
    }
}
