using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using YandexSpeech;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using Newtonsoft.Json.Linq;

namespace YandexSpeech.Tests
{
    public class AdminYooMoneyControllerTests
    {
        [Fact]
        public async Task ApplyPaymentOperation_ActivatesSubscriptionAndMarksOperationSucceeded()
        {
            var options = new DbContextOptionsBuilder<MyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new MyDbContext(options);

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

            var controller = new AdminYooMoneyController(
                new YooMoneyRepositoryStub(),
                dbContext,
                new PaymentGatewayService(dbContext, NullLogger<PaymentGatewayService>.Instance),
                new WalletService(dbContext, NullLogger<WalletService>.Instance),
                new SubscriptionService(dbContext, NullLogger<SubscriptionService>.Instance));

            var result = await controller.ApplyPaymentOperation(operation.Id.ToString());

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var details = Assert.IsType<AdminPaymentOperationDetailsDto>(okResult.Value);
            Assert.True(details.Applied);
            Assert.Equal(PaymentOperationStatus.Succeeded.ToString(), details.Status);

            var updatedOperation = await dbContext.PaymentOperations.FirstAsync();
            Assert.Equal(PaymentOperationStatus.Succeeded, updatedOperation.Status);
            Assert.Null(updatedOperation.WalletTransactionId);

            var subscription = await dbContext.UserSubscriptions.FirstOrDefaultAsync();
            Assert.NotNull(subscription);
            Assert.Equal(planId, subscription!.PlanId);

            var invoice = await dbContext.SubscriptionInvoices.FirstOrDefaultAsync();
            Assert.NotNull(invoice);
            Assert.Equal(subscription.Id, invoice!.UserSubscriptionId);
            Assert.Equal(operation.Amount, invoice.Amount);

            var updatedUser = await dbContext.Users.FirstAsync();
            Assert.Equal(subscription.Id, updatedUser.CurrentSubscriptionId);
        }

        [Fact]
        public async Task GetBillDetails_ReturnsConvertedDictionary()
        {
            var repository = new BillDetailsYooMoneyRepositoryStub
            {
                Result = new BillDetails
                {
                    Data = new Dictionary<string, JToken>
                    {
                        ["invoice_id"] = JToken.FromObject("inv-1"),
                        ["amount"] = JToken.FromObject(42.5m),
                        ["nested"] = JObject.FromObject(new { key = "value" }),
                        ["items"] = JArray.FromObject(new[] { "a", "b" })
                    }
                }
            };

            var options = new DbContextOptionsBuilder<MyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new MyDbContext(options);

            var controller = new AdminYooMoneyController(
                repository,
                dbContext,
                new PaymentGatewayService(dbContext, NullLogger<PaymentGatewayService>.Instance),
                new WalletService(dbContext, NullLogger<WalletService>.Instance),
                new SubscriptionService(dbContext, NullLogger<SubscriptionService>.Instance));

            var result = await controller.GetBillDetails("bill-123");

            Assert.Equal("bill-123", repository.LastBillId);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var details = Assert.IsAssignableFrom<IDictionary<string, object?>>(okResult.Value);

            Assert.Equal("inv-1", details["invoice_id"]);
            Assert.Equal(42.5m, details["amount"]);

            var nested = Assert.IsType<Dictionary<string, object?>>(details["nested"]);
            Assert.Equal("value", nested["key"]);

            var items = Assert.IsType<List<object?>>(details["items"]);
            Assert.Contains("a", items);
            Assert.Contains("b", items);
        }

        [Fact]
        public async Task GetBillDetails_ReturnsNotFound_WhenRepositoryReturnsNull()
        {
            var repository = new BillDetailsYooMoneyRepositoryStub();

            var options = new DbContextOptionsBuilder<MyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new MyDbContext(options);

            var controller = new AdminYooMoneyController(
                repository,
                dbContext,
                new PaymentGatewayService(dbContext, NullLogger<PaymentGatewayService>.Instance),
                new WalletService(dbContext, NullLogger<WalletService>.Instance),
                new SubscriptionService(dbContext, NullLogger<SubscriptionService>.Instance));

            var result = await controller.GetBillDetails("missing");

            Assert.Equal("missing", repository.LastBillId);
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task GetBillDetails_ReturnsNotFound_WhenRepositoryThrowsNotFound()
        {
            var repository = new BillDetailsYooMoneyRepositoryStub
            {
                ExceptionToThrow = new HttpRequestException("Not found", null, HttpStatusCode.NotFound)
            };

            var options = new DbContextOptionsBuilder<MyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new MyDbContext(options);

            var controller = new AdminYooMoneyController(
                repository,
                dbContext,
                new PaymentGatewayService(dbContext, NullLogger<PaymentGatewayService>.Instance),
                new WalletService(dbContext, NullLogger<WalletService>.Instance),
                new SubscriptionService(dbContext, NullLogger<SubscriptionService>.Instance));

            var result = await controller.GetBillDetails("missing");

            Assert.Equal("missing", repository.LastBillId);
            Assert.IsType<NotFoundResult>(result.Result);
        }

        private sealed class YooMoneyRepositoryStub : IYooMoneyRepository
        {
            public Task<string> AuthorizeAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task<string> ExchangeTokenAsync(string code, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task<OperationDetails?> GetOperationDetailsAsync(string operationId, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<OperationDetails?>(null);

            public Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryAsync(int from, int count, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<OperationHistory>?>(Array.Empty<OperationHistory>());

            public Task<BillDetails?> GetBillDetailsAsync(string billId, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult<BillDetails?>(null);
        }

        private sealed class BillDetailsYooMoneyRepositoryStub : IYooMoneyRepository
        {
            public BillDetails? Result { get; set; }

            public Exception? ExceptionToThrow { get; set; }

            public string? LastBillId { get; private set; }

            public Task<string> AuthorizeAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task<string> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

            public Task<OperationDetails?> GetOperationDetailsAsync(string operationId, CancellationToken cancellationToken = default) => Task.FromResult<OperationDetails?>(null);

            public Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryAsync(int from, int count, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<OperationHistory>?>(Array.Empty<OperationHistory>());

            public Task<BillDetails?> GetBillDetailsAsync(string billId, CancellationToken cancellationToken = default)
            {
                LastBillId = billId;

                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                return Task.FromResult(Result);
            }
        }
    }
}
