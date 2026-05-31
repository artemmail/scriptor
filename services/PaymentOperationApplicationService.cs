using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public sealed class PaymentOperationApplicationService : IPaymentOperationApplicationService
    {
        private readonly MyDbContext _dbContext;
        private readonly IPaymentGatewayService _paymentGatewayService;
        private readonly IWalletService _walletService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<PaymentOperationApplicationService> _logger;

        public PaymentOperationApplicationService(
            MyDbContext dbContext,
            IPaymentGatewayService paymentGatewayService,
            IWalletService walletService,
            ISubscriptionService subscriptionService,
            ILogger<PaymentOperationApplicationService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _paymentGatewayService = paymentGatewayService ?? throw new ArgumentNullException(nameof(paymentGatewayService));
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PaymentOperation?> ApplyAsync(
            Guid operationId,
            string? reference = null,
            string? externalPayload = null,
            CancellationToken cancellationToken = default)
        {
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation identifier is required.", nameof(operationId));
            }

            var operation = await _dbContext.PaymentOperations
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken)
                .ConfigureAwait(false);

            if (operation == null)
            {
                return null;
            }

            if (operation.Status == PaymentOperationStatus.Succeeded)
            {
                return operation;
            }

            if (operation.Status == PaymentOperationStatus.Failed || operation.Status == PaymentOperationStatus.Cancelled)
            {
                throw new InvalidOperationException("Эту операцию нельзя применить, потому что она завершилась неуспешно.");
            }

            if (string.IsNullOrWhiteSpace(operation.UserId))
            {
                throw new InvalidOperationException("У операции отсутствует пользователь для применения.");
            }

            if (operation.Amount <= 0m)
            {
                throw new InvalidOperationException("Сумма операции должна быть больше нуля для применения.");
            }

            var payload = PaymentOperationPayloadSerializer.Deserialize(operation.Payload);
            var resolvedReference = ResolveReference(operation, reference);

            if (payload == null || string.Equals(payload.Type, PaymentOperationPayloadTypes.Wallet, StringComparison.OrdinalIgnoreCase))
            {
                return await ApplyWalletDepositAsync(operation, payload, resolvedReference, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(payload.Type, PaymentOperationPayloadTypes.Subscription, StringComparison.OrdinalIgnoreCase))
            {
                return await ApplySubscriptionPaymentAsync(operation, payload, resolvedReference, externalPayload, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Применение поддерживается только для операций пополнения кошелька или подписки.");
        }

        private async Task<PaymentOperation> ApplyWalletDepositAsync(
            PaymentOperation operation,
            PaymentOperationPayload? payload,
            string reference,
            CancellationToken cancellationToken)
        {
            var walletTransaction = await FindExistingWalletTransactionAsync(operation, reference, cancellationToken).ConfigureAwait(false);
            if (walletTransaction == null)
            {
                var comment = string.IsNullOrWhiteSpace(payload?.Comment)
                    ? "Пополнение счёта"
                    : payload.Comment!;

                walletTransaction = await _walletService
                    .DepositAsync(
                        operation.UserId,
                        operation.Amount,
                        operation.Currency,
                        operation.Id,
                        reference,
                        comment,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var updatedOperation = await _paymentGatewayService
                .MarkSucceededAsync(operation.Id, reference, walletTransaction.Id, cancellationToken)
                .ConfigureAwait(false);

            await EnsureUserLoadedAsync(updatedOperation, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Applied wallet payment operation {OperationId} using reference {Reference}",
                operation.Id,
                reference);

            return updatedOperation;
        }

        private async Task<PaymentOperation> ApplySubscriptionPaymentAsync(
            PaymentOperation operation,
            PaymentOperationPayload payload,
            string reference,
            string? externalPayload,
            CancellationToken cancellationToken)
        {
            if (payload.PlanId == null || payload.PlanId == Guid.Empty)
            {
                throw new InvalidOperationException("В данных операции отсутствует идентификатор плана подписки.");
            }

            var subscription = await _dbContext.UserSubscriptions
                .FirstOrDefaultAsync(
                    s => s.UserId == operation.UserId
                        && s.PlanId == payload.PlanId.Value
                        && s.ExternalPaymentId == reference,
                    cancellationToken)
                .ConfigureAwait(false);

            if (subscription == null)
            {
                subscription = await _subscriptionService
                    .ActivateSubscriptionAsync(
                        operation.UserId,
                        payload.PlanId.Value,
                        externalPaymentId: reference,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            var invoice = await _dbContext.SubscriptionInvoices
                .FirstOrDefaultAsync(
                    i => i.UserSubscriptionId == subscription.Id && i.ExternalInvoiceId == reference,
                    cancellationToken)
                .ConfigureAwait(false);

            if (invoice == null)
            {
                var paidAt = DateTime.UtcNow;
                invoice = new SubscriptionInvoice
                {
                    Id = Guid.NewGuid(),
                    UserSubscriptionId = subscription.Id,
                    Amount = operation.Amount,
                    Currency = operation.Currency,
                    Status = SubscriptionInvoiceStatus.Paid,
                    IssuedAt = paidAt,
                    PaidAt = paidAt,
                    PaymentProvider = operation.Provider.ToString(),
                    ExternalInvoiceId = reference,
                    Payload = string.IsNullOrWhiteSpace(externalPayload) ? operation.Payload : externalPayload
                };

                _dbContext.SubscriptionInvoices.Add(invoice);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var updatedOperation = await _paymentGatewayService
                .MarkSucceededAsync(operation.Id, reference, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await EnsureUserLoadedAsync(updatedOperation, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Applied subscription payment operation {OperationId} using reference {Reference}",
                operation.Id,
                reference);

            return updatedOperation;
        }

        private async Task<WalletTransaction?> FindExistingWalletTransactionAsync(
            PaymentOperation operation,
            string reference,
            CancellationToken cancellationToken)
        {
            if (operation.WalletTransactionId.HasValue)
            {
                var walletTransaction = await _dbContext.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Id == operation.WalletTransactionId.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (walletTransaction != null)
                {
                    return walletTransaction;
                }
            }

            return await _dbContext.WalletTransactions
                .FirstOrDefaultAsync(
                    t => t.RelatedEntityId == operation.Id
                        && t.Type == WalletTransactionType.Deposit
                        && t.Reference == reference,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static string ResolveReference(PaymentOperation operation, string? reference)
        {
            if (!string.IsNullOrWhiteSpace(reference))
            {
                return reference.Trim();
            }

            if (!string.IsNullOrWhiteSpace(operation.ExternalOperationId))
            {
                return operation.ExternalOperationId!;
            }

            return operation.Id.ToString();
        }

        private async Task EnsureUserLoadedAsync(PaymentOperation operation, CancellationToken cancellationToken)
        {
            var userReference = _dbContext.Entry(operation).Reference(o => o.User);
            if (!userReference.IsLoaded)
            {
                await userReference.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
