using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public static class SubscriptionPlanSeeder
    {
        private sealed record PlanSeed(
            string Code,
            string Name,
            string Description,
            decimal Price,
            int IncludedTranscriptionMinutes,
            int IncludedVideos,
            int Priority);

        private static readonly IReadOnlyList<PlanSeed> DefaultPlans = new List<PlanSeed>
        {
            new(
                Code: "welcome_free",
                Name: "Стартовый пакет",
                Description: "Бесплатные минуты и видео после регистрации.",
                Price: 0m,
                IncludedTranscriptionMinutes: 60,
                IncludedVideos: 3,
                Priority: 10),
            new(
                Code: "credits_300",
                Name: "Пакет 300",
                Description: "5 часов транскрибации и 10 видео.",
                Price: 300m,
                IncludedTranscriptionMinutes: 300,
                IncludedVideos: 10,
                Priority: 20),
            new(
                Code: "credits_1000",
                Name: "Пакет 1000",
                Description: "20 часов транскрибации и 40 видео.",
                Price: 1000m,
                IncludedTranscriptionMinutes: 1200,
                IncludedVideos: 40,
                Priority: 30)
            ,
            new(
                Code: "credits_3000",
                Name: "Пакет 3000",
                Description: "80 часов транскрибации и 160 видео.",
                Price: 3000m,
                IncludedTranscriptionMinutes: 4800,
                IncludedVideos: 160,
                Priority: 40)
        };

        public static async Task EnsureDefaultPlansAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("SubscriptionPlanSeeder");

            foreach (var seed in DefaultPlans)
            {
                var plan = await dbContext.SubscriptionPlans
                    .FirstOrDefaultAsync(p => p.Code == seed.Code, cancellationToken)
                    .ConfigureAwait(false);

                if (plan == null)
                {
                    plan = new SubscriptionPlan
                    {
                        Id = Guid.NewGuid(),
                        Code = seed.Code,
                        CreatedAt = DateTime.UtcNow,
                        Name = seed.Name,
                        Description = seed.Description,
                        BillingPeriod = SubscriptionBillingPeriod.OneTime,
                        Price = seed.Price,
                        Currency = "RUB",
                        IncludedTranscriptionMinutes = seed.IncludedTranscriptionMinutes,
                        IncludedVideos = seed.IncludedVideos,
                        CanHideCaptions = true,
                        IsUnlimitedRecognitions = false,
                        Priority = seed.Priority,
                        IsActive = true,
                        UpdatedAt = DateTime.UtcNow
                    };
                    dbContext.SubscriptionPlans.Add(plan);
                    logger.LogInformation("Created subscription plan {Code}", seed.Code);
                    continue;
                }

                plan.Name = seed.Name;
                plan.Description = seed.Description;
                plan.Price = seed.Price;
                plan.Currency = "RUB";
                plan.BillingPeriod = SubscriptionBillingPeriod.OneTime;
                plan.IncludedTranscriptionMinutes = seed.IncludedTranscriptionMinutes;
                plan.IncludedVideos = seed.IncludedVideos;
                plan.CanHideCaptions = true;
                plan.IsUnlimitedRecognitions = false;
                plan.Priority = seed.Priority;
                plan.IsActive = true;
                plan.UpdatedAt = DateTime.UtcNow;
            }

            var defaultCodes = new HashSet<string>(DefaultPlans.Select(p => p.Code), StringComparer.OrdinalIgnoreCase);
            var legacyPlans = await dbContext.SubscriptionPlans
                .Where(p => !defaultCodes.Contains(p.Code))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var plan in legacyPlans)
            {
                if (!plan.IsActive)
                {
                    continue;
                }

                plan.IsActive = false;
                plan.UpdatedAt = DateTime.UtcNow;
                logger.LogInformation("Deactivated legacy subscription plan {Code}", plan.Code);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
