using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;

namespace YandexSpeech.Tests
{
    public class YooMoneyAutoActivationServiceTests
    {
        [Fact]
        public async Task ProcessAsync_AppliesSubscription_WhenYooMoneyLabelMatchesPendingOperation()
        {
            var (dbContext, operation, planId) = await CreatePendingSubscriptionOperationAsync();

            var externalOperationId = "ym-ext-1";
            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = externalOperationId,
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                new OperationDetails
                {
                    OperationId = externalOperationId,
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                });

            var service = CreateAutoActivationService(dbContext, repository);

            var appliedCount = await service.ProcessAsync();

            Assert.Equal(1, appliedCount);

            var updatedOperation = await dbContext.PaymentOperations.FirstAsync();
            Assert.Equal(PaymentOperationStatus.Succeeded, updatedOperation.Status);
            Assert.Equal(externalOperationId, updatedOperation.ExternalOperationId);

            var subscription = await dbContext.UserSubscriptions.FirstOrDefaultAsync();
            Assert.NotNull(subscription);
            Assert.Equal(planId, subscription!.PlanId);

            var invoice = await dbContext.SubscriptionInvoices.FirstOrDefaultAsync();
            Assert.NotNull(invoice);
            Assert.Equal(externalOperationId, invoice!.ExternalInvoiceId);
        }

        [Fact]
        public async Task ProcessAsync_IgnoresSharedWalletPayment_WhenGuidBelongsToAnotherSystem()
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();

            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-ext-2",
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData(Guid.NewGuid().ToString())
                },
                new OperationDetails
                {
                    OperationId = "ym-ext-2",
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData(Guid.NewGuid().ToString())
                });

            var service = CreateAutoActivationService(dbContext, repository);

            var appliedCount = await service.ProcessAsync();

            Assert.Equal(0, appliedCount);

            var updatedOperation = await dbContext.PaymentOperations.FirstAsync();
            Assert.Equal(PaymentOperationStatus.Pending, updatedOperation.Status);
            Assert.Null(updatedOperation.ExternalOperationId);
            Assert.Empty(await dbContext.UserSubscriptions.ToListAsync());
            Assert.Empty(await dbContext.SubscriptionInvoices.ToListAsync());
        }

        [Fact]
        public async Task ProcessAsync_IgnoresPayment_WhenLabelIsNotGuid()
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();

            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-ext-3",
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData("not-a-guid")
                },
                new OperationDetails
                {
                    OperationId = "ym-ext-3",
                    Amount = operation.Amount,
                    Status = "success",
                    DateTime = DateTime.UtcNow,
                    AdditionalData = CreateAdditionalData("not-a-guid")
                });

            var service = CreateAutoActivationService(dbContext, repository);

            var appliedCount = await service.ProcessAsync();

            Assert.Equal(0, appliedCount);

            var updatedOperation = await dbContext.PaymentOperations.FirstAsync();
            Assert.Equal(PaymentOperationStatus.Pending, updatedOperation.Status);
            Assert.Null(updatedOperation.ExternalOperationId);
            Assert.Empty(await dbContext.UserSubscriptions.ToListAsync());
            Assert.Empty(await dbContext.SubscriptionInvoices.ToListAsync());
        }

        private static YooMoneyAutoActivationService CreateAutoActivationService(
            MyDbContext dbContext,
            IYooMoneyRepository repository)
        {
            var paymentOperationApplicationService = new PaymentOperationApplicationService(
                dbContext,
                new PaymentGatewayService(dbContext, NullLogger<PaymentGatewayService>.Instance),
                new WalletService(dbContext, NullLogger<WalletService>.Instance),
                new SubscriptionService(dbContext, NullLogger<SubscriptionService>.Instance),
                NullLogger<PaymentOperationApplicationService>.Instance);

            return new YooMoneyAutoActivationService(
                repository,
                dbContext,
                paymentOperationApplicationService,
                NullLogger<YooMoneyAutoActivationService>.Instance);
        }

        private static async Task<(MyDbContext DbContext, PaymentOperation Operation, Guid PlanId)> CreatePendingSubscriptionOperationAsync()
        {
            var options = new DbContextOptionsBuilder<MyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var dbContext = new MyDbContext(options);

            var user = new ApplicationUser
            {
                Id = "user-1",
                Email = "user@example.com",
                UserName = "user@example.com"
            };

            var planId = Guid.NewGuid();
            var plan = new SubscriptionPlan
            {
                Id = planId,
                Code = "test-plan",
                Name = "Test plan",
                BillingPeriod = SubscriptionBillingPeriod.Monthly,
                Price = 123m,
                Currency = "RUB",
                IncludedTranscriptionMinutes = 120,
                IsActive = true
            };

            var operation = new PaymentOperation
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = PaymentProvider.YooMoney,
                Amount = 123m,
                Currency = "RUB",
                Status = PaymentOperationStatus.Pending,
                RequestedAt = DateTime.UtcNow,
                Payload = JsonSerializer.Serialize(new { type = "subscription", planId })
            };

            dbContext.Users.Add(user);
            dbContext.SubscriptionPlans.Add(plan);
            dbContext.PaymentOperations.Add(operation);
            await dbContext.SaveChangesAsync();

            return (dbContext, operation, planId);
        }

        private static IDictionary<string, JToken> CreateAdditionalData(string label)
        {
            return new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = JToken.FromObject(label),
                ["direction"] = JToken.FromObject("in")
            };
        }

        private sealed class YooMoneyRepositoryStub : IYooMoneyRepository
        {
            private readonly IReadOnlyList<OperationHistory> _operationHistory;
            private readonly Dictionary<string, OperationDetails> _operationDetails;

            public YooMoneyRepositoryStub(OperationHistory operationHistory, OperationDetails operationDetails)
            {
                _operationHistory = new[] { operationHistory };
                _operationDetails = new Dictionary<string, OperationDetails>(StringComparer.OrdinalIgnoreCase)
                {
                    [operationDetails.OperationId ?? string.Empty] = operationDetails
                };
            }

            public Task<string> AuthorizeAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(string.Empty);
            }

            public Task<string> ExchangeTokenAsync(string code, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(string.Empty);
            }

            public Task<OperationDetails?> GetOperationDetailsAsync(string operationId, System.Threading.CancellationToken cancellationToken = default)
            {
                _operationDetails.TryGetValue(operationId, out var details);
                return Task.FromResult<OperationDetails?>(details);
            }

            public Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryAsync(int from, int count, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<OperationHistory>?>(_operationHistory);
            }
        }
    }
}
