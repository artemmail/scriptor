using System;
using System.Collections.Generic;
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
            SubscriptionBillingPeriod Period,
            decimal Price,
            bool CanHideCaptions,
            bool IsUnlimitedRecognitions,
            int Priority);

        private static readonly IReadOnlyList<PlanSeed> DefaultPlans = new List<PlanSeed>
        {
            new(
                Code: "recognition_unlimited_3_days",
                Name: "Без ограничений на распознавание (3 дня)",
                Description: "Позволяет снять дневные ограничения на распознавание на трое суток.",
                Period: SubscriptionBillingPeriod.ThreeDays,
                Price: 490m,
                CanHideCaptions: false,
                IsUnlimitedRecognitions: true,
                Priority: 15),
            new(
                Code: "recognition_unlimited_month",
                Name: "Без ограничений на распознавание (1 месяц)",
                Description: "Снимает дневные ограничения на распознавание на один месяц.",
                Period: SubscriptionBillingPeriod.Monthly,
                Price: 890m,
                CanHideCaptions: false,
                IsUnlimitedRecognitions: true,
                Priority: 20),
            new(
                Code: "recognition_unlimited_year",
                Name: "Без ограничений на распознавание (1 год)",
                Description: "Снимает дневные ограничения на распознавание на один год.",
                Period: SubscriptionBillingPeriod.Yearly,
                Price: 8990m,
                CanHideCaptions: false,
                IsUnlimitedRecognitions: true,
                Priority: 30)
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
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.SubscriptionPlans.Add(plan);
                    logger.LogInformation("Created subscription plan {Code}", seed.Code);
                }

                plan.Name = seed.Name;
                plan.Description = seed.Description;
                plan.BillingPeriod = seed.Period;
                plan.Price = seed.Price;
                plan.Currency = "RUB";
                plan.CanHideCaptions = seed.CanHideCaptions;
                plan.IsUnlimitedRecognitions = seed.IsUnlimitedRecognitions;
                plan.Priority = seed.Priority;
                plan.IsActive = true;
                plan.UpdatedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
