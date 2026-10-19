using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;
using YandexSpeech;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.Tests
{
    public class YooMoneyAutoActivationServiceTests
    {
        [Fact]
        public void UniqueExternalOperationMigrationIsDiscoverable()
        {
            using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>()
                .UseSqlServer("Server=unused;Database=unused;Integrated Security=True")
                .Options);
            Assert.Contains("20261003000002_UniqueYooMoneyOperation", db.Database.GetMigrations());
        }

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
        public async Task ProcessAsync_AppliesOldWalletPaymentOnce_WhenAbsentFromRecentHistory()
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();
            operation.Payload = PaymentOperationPayloadSerializer.Serialize(new PaymentOperationPayload
            {
                Type = PaymentOperationPayloadTypes.Wallet
            });
            await dbContext.SaveChangesAsync();

            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-wallet-1", Amount = operation.Amount, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                new OperationDetails
                {
                    OperationId = "ym-wallet-1", Amount = operation.Amount, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                includeInRecentHistory: false);

            var service = CreateAutoActivationService(dbContext, repository);
            Assert.Equal(1, await service.ProcessAsync());
            Assert.Equal(0, await service.ProcessAsync());

            var wallet = await dbContext.UserWallets.SingleAsync();
            Assert.Equal(operation.Amount, wallet.Balance);
            Assert.Single(await dbContext.WalletTransactions.ToListAsync());
            Assert.Equal(PaymentOperationStatus.Succeeded, (await dbContext.PaymentOperations.SingleAsync()).Status);
        }

        [Fact]
        public async Task ProcessAsync_DoesNotApply_WhenVerifiedLabelDisagrees()
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();
            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-unverified", Amount = operation.Amount, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                new OperationDetails
                {
                    OperationId = "ym-unverified", Amount = operation.Amount, Status = "success",
                    AdditionalData = CreateAdditionalData(Guid.NewGuid().ToString())
                });

            var service = CreateAutoActivationService(dbContext, repository);
            Assert.Equal(0, await service.ProcessAsync());
            Assert.Equal(PaymentOperationStatus.Pending, (await dbContext.PaymentOperations.SingleAsync()).Status);
            Assert.Empty(await dbContext.UserSubscriptions.ToListAsync());
        }

        [Theory]
        [InlineData(false, 970)]
        [InlineData(false, 950)]
        [InlineData(true, 970)]
        public async Task ProcessAsync_AppliesOrderOnce_WhenCommissionReducesCreditedAmount(bool walletPayment, int creditedAmount)
        {
            var (dbContext, operation, planId) = await CreatePendingSubscriptionOperationAsync();
            await using var db = dbContext;
            operation.Amount = 1000m;
            (await db.SubscriptionPlans.SingleAsync()).Price = 1000m;
            if (walletPayment)
                operation.Payload = PaymentOperationPayloadSerializer.Serialize(new PaymentOperationPayload
                {
                    Type = PaymentOperationPayloadTypes.Wallet
                });
            await db.SaveChangesAsync();

            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-commission", Amount = creditedAmount, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                new OperationDetails
                {
                    OperationId = "ym-commission", Amount = creditedAmount, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                });

            var service = CreateAutoActivationService(db, repository);
            Assert.Equal(1, await service.ProcessAsync());
            Assert.Equal(0, await service.ProcessAsync());
            Assert.Equal(PaymentOperationStatus.Succeeded, operation.Status);
            Assert.Equal("ym-commission", operation.ExternalOperationId);
            if (walletPayment)
            {
                Assert.Equal(1000m, (await db.UserWallets.SingleAsync()).Balance);
                Assert.Single(await db.WalletTransactions.ToListAsync());
            }
            else
            {
                Assert.Equal(planId, (await db.UserSubscriptions.SingleAsync()).PlanId);
                Assert.Equal(1000m, (await db.SubscriptionInvoices.SingleAsync()).Amount);
            }
        }

        [Theory]
        [InlineData("in_progress", "in", 970)]
        [InlineData("success", "out", 970)]
        [InlineData("success", "in", 0)]
        [InlineData("success", "in", -1)]
        public async Task ProcessAsync_DoesNotApply_WhenDetailsDoNotConfirmIncomingPayment(string status, string direction, int amount)
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();
            await using var db = dbContext;
            var detailsData = CreateAdditionalData(operation.Id.ToString());
            detailsData["direction"] = JToken.FromObject(direction);
            var repository = new YooMoneyRepositoryStub(
                new OperationHistory
                {
                    OperationId = "ym-invalid", Amount = 970m, Status = "success",
                    AdditionalData = CreateAdditionalData(operation.Id.ToString())
                },
                new OperationDetails
                {
                    OperationId = "ym-invalid", Amount = amount, Status = status,
                    AdditionalData = detailsData
                });

            Assert.Equal(0, await CreateAutoActivationService(db, repository).ProcessAsync());
            Assert.Equal(PaymentOperationStatus.Pending, operation.Status);
            Assert.Empty(await db.UserSubscriptions.ToListAsync());
            Assert.Empty(await db.SubscriptionInvoices.ToListAsync());
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Notification_WithCommission_RequiresValidSignatureAndAppliesOnlyOnce(bool validSignature)
        {
            var (dbContext, operation, _) = await CreatePendingSubscriptionOperationAsync();
            await using var db = dbContext;
            operation.Amount = 1000m;
            await db.SaveChangesAsync();
            var gateway = new PaymentGatewayService(db, NullLogger<PaymentGatewayService>.Instance);
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var subscriptions = new SubscriptionService(db, NullLogger<SubscriptionService>.Instance);
            var application = new PaymentOperationApplicationService(db, gateway, wallet, subscriptions,
                NullLogger<PaymentOperationApplicationService>.Instance);
            var controller = new PaymentsController(db, gateway, subscriptions, wallet, application,
                Options.Create(new YooMoneyOptions { NotificationSecret = "test-secret" }),
                Options.Create(new SubscriptionLimitsOptions()), NullLogger<PaymentsController>.Instance);
            var notification = new PaymentsController.YooMoneyNotification
            {
                NotificationType = "card-incoming", OperationId = "ym-notification",
                Amount = 970m, Currency = "643", DateTime = "2026-10-19T13:10:00Z",
                Sender = "", Label = operation.Id.ToString()
            };
            var signatureInput = $"card-incoming&ym-notification&970.00&643&2026-10-19T13:10:00Z&&false&test-secret&{notification.Label}";
            notification.Sha1Hash = validSignature
                ? Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(signatureInput))).ToLowerInvariant()
                : "invalid";

            var result = await controller.HandleYooMoneyNotification(notification, default);
            if (!validSignature)
            {
                Assert.IsType<BadRequestObjectResult>(result);
                Assert.Equal(PaymentOperationStatus.Pending, operation.Status);
                Assert.Empty(await db.SubscriptionInvoices.ToListAsync());
                return;
            }

            Assert.IsType<OkResult>(result);
            Assert.IsType<OkResult>(await controller.HandleYooMoneyNotification(notification, default));
            Assert.Equal(PaymentOperationStatus.Succeeded, operation.Status);
            Assert.Equal("ym-notification", operation.ExternalOperationId);
            Assert.Equal(1000m, (await db.SubscriptionInvoices.SingleAsync()).Amount);
            Assert.Single(await db.UserSubscriptions.ToListAsync());
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
            private readonly bool _includeInRecentHistory;

            public YooMoneyRepositoryStub(OperationHistory operationHistory, OperationDetails operationDetails, bool includeInRecentHistory = true)
            {
                _operationHistory = new[] { operationHistory };
                _includeInRecentHistory = includeInRecentHistory;
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
                return Task.FromResult<IReadOnlyList<OperationHistory>?>(
                    _includeInRecentHistory ? _operationHistory : Array.Empty<OperationHistory>());
            }

            public Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryByLabelAsync(string label, System.Threading.CancellationToken cancellationToken = default)
            {
                var matching = _operationHistory
                    .Where(operation => string.Equals(operation.AdditionalData?["label"]?.ToString(), label, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return Task.FromResult<IReadOnlyList<OperationHistory>?>(matching);
            }
        }
    }
}
