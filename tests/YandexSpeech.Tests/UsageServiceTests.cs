using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech.models.DB;
using YandexSpeech.services;

namespace YandexSpeech.Tests;

public class UsageServiceTests
{
    [Fact]
    public async Task EvaluateDailyQuotaAsync_CountsLast24HourRecognitions()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new MyDbContext(options);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-test",
            Email = "user@example.com"
        });

        var now = DateTime.UtcNow;

        dbContext.YoutubeCaptionTasks.AddRange(
            new YoutubeCaptionTask
            {
                Id = "recent-completed",
                UserId = "user-test",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddHours(-1),
                ModifiedAt = now.AddMinutes(-30)
            },
            new YoutubeCaptionTask
            {
                Id = "exclude-status",
                UserId = "user-test",
                Done = true,
                Status = (RecognizeStatus)990,
                CreatedAt = now.AddMinutes(-10),
                ModifiedAt = now.AddMinutes(-5)
            },
            new YoutubeCaptionTask
            {
                Id = "old",
                UserId = "user-test",
                Done = true,
                Status = RecognizeStatus.Done,
                CreatedAt = now.AddDays(-2),
                ModifiedAt = now.AddDays(-2)
            },
            new YoutubeCaptionTask
            {
                Id = "not-done",
                UserId = "user-test",
                Done = false,
                Status = RecognizeStatus.InProgress,
                CreatedAt = now
            });

        await dbContext.SaveChangesAsync();

        var service = new UsageService(dbContext, NullLogger<UsageService>.Instance);

        var result = await service.EvaluateDailyQuotaAsync("user-test", 1, 3, false);

        Assert.True(result.IsAllowed);
        Assert.Equal(1, result.RemainingQuota);
    }
}
