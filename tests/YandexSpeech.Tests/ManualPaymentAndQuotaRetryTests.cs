using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;

namespace YandexSpeech.Tests;

public sealed class ManualPaymentAndQuotaRetryTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Enqueue_RetriesQuotaFailureOnlyBeforeCreditsWereCharged(bool charged, bool shouldRetry)
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new MyDbContext(options));
        await using var provider = services.BuildServiceProvider();
        await using (var db = new MyDbContext(options))
        {
            db.YoutubeCaptionTasks.Add(new YoutubeCaptionTask
            {
                Id = "dQw4w9WgXc", UserId = "old-user", Result = "",
                Status = RecognizeStatus.Error, Done = true,
                Error = "Недостаточно видео-кредитов для запуска задачи. Доступно: 0. Пополните пакет в биллинге.",
                QuotaChargedAt = charged ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
        }
        var manager = new CaptionTaskManager(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CaptionTaskManager>.Instance);
        await manager.EnqueueCaptionTaskAsync("dQw4w9WgXc", "127.0.0.1", "paid-user");
        await using var check = new MyDbContext(options);
        var task = await check.YoutubeCaptionTasks.SingleAsync();
        Assert.Equal(shouldRetry ? "paid-user" : "old-user", task.UserId);
        Assert.Equal(shouldRetry ? RecognizeStatus.Created : RecognizeStatus.Error, task.Status);
        Assert.Equal(!shouldRetry, task.Done);
        if (shouldRetry) Assert.Null(task.Error);
    }

    [Fact]
    public async Task ManualPayment_GrantsCreditsAndSavesInvoice_RejectsExpiredEndDate()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new MyDbContext(options);
        db.Users.Add(new ApplicationUser { Id = "paid-user" });
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Code = "credits_3000", Name = "Пакет 3000", Price = 3000,
            IncludedVideos = 160, IncludedTranscriptionMinutes = 4800
        });
        await db.SaveChangesAsync();
        var service = new SubscriptionService(db, NullLogger<SubscriptionService>.Instance);
        var controller = new AdminSubscriptionsController(db, service);
        var request = new ManualSubscriptionPaymentRequest
        {
            UserId = "paid-user", PlanCode = "credits_3000", EndDate = DateTime.UtcNow.AddDays(-1)
        };
        var rejected = await controller.CreateManualSubscription(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(rejected.Result);
        Assert.Empty(await db.UserSubscriptions.ToListAsync());
        request.EndDate = null;
        var saved = await controller.CreateManualSubscription(request, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(saved.Result);
        Assert.Equal(3000m, (await db.SubscriptionInvoices.SingleAsync()).Amount);
        Assert.Equal(160, (await service.GetQuotaBalanceAsync("paid-user")).RemainingVideos);
        Assert.True(await service.TryConsumeQuotaAsync("paid-user", 0, 1));
        Assert.Equal(159, (await service.GetQuotaBalanceAsync("paid-user")).RemainingVideos);
    }
}
