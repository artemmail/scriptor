using System;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Models;
using YandexSpeech.services.Options;

namespace YandexSpeech.Tests;

public class OpenAiTranscriptionDeletionTests
{
    [Theory]
    [InlineData("admin", true, true)]
    [InlineData("alice", false, true)]
    [InlineData("bob", false, false)]
    public async Task FailedTaskCanBeDeletedByOwnerOrAdminOnly(string userId, bool isAdmin, bool canDelete)
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MyDbContext(options);
        db.OpenAiTranscriptionTasks.Add(new OpenAiTranscriptionTask
        {
            Id = "failed-task",
            CreatedBy = "alice",
            SourceFilePath = Path.Combine(Path.GetTempPath(), $"missing-transcription-{Guid.NewGuid():N}.wav"),
            Status = OpenAiTranscriptionStatus.Error,
            Steps =
            [
                new OpenAiTranscriptionStep
                {
                    TaskId = "failed-task",
                    Step = OpenAiTranscriptionStatus.Transcribing,
                    Status = OpenAiTranscriptionStepStatus.Error
                }
            ],
            Segments =
            [
                new OpenAiRecognizedSegment
                {
                    TaskId = "failed-task",
                    Text = "partial result"
                }
            ]
        });
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, isAdmin ? "Admin" : "User")
        };
        var controller = new OpenAiTranscriptionController(
            null!, db, null!, null!, NullLogger<OpenAiTranscriptionController>.Instance,
            null!, null!, null!, new UnusedSubscriptionAccessService(),
            Options.Create(new EventBusOptions()), new FfmpegService(new ConfigurationBuilder().Build()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                }
            }
        };

        var result = await controller.Delete("failed-task");

        if (canDelete)
        {
            Assert.IsType<NoContentResult>(result);
            Assert.Empty(await db.OpenAiTranscriptionTasks.ToListAsync());
            Assert.Empty(await db.OpenAiTranscriptionSteps.ToListAsync());
            Assert.Empty(await db.OpenAiRecognizedSegments.ToListAsync());
        }
        else
        {
            Assert.IsType<NotFoundResult>(result);
            Assert.Single(await db.OpenAiTranscriptionTasks.ToListAsync());
        }
    }

    private sealed class UnusedSubscriptionAccessService : ISubscriptionAccessService
    {
        public Task<UsageDecision> AuthorizeYoutubeRecognitionAsync(
            string userId, int requestedVideos = 1, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<UsageDecision> AuthorizeTranscriptionAsync(
            string userId, int requestedTranscriptionMinutes, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
