using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public class PaymentGatewayService : IPaymentGatewayService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<PaymentGatewayService> _logger;

        public PaymentGatewayService(MyDbContext dbContext, ILogger<PaymentGatewayService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PaymentOperation> RegisterOperationAsync(string userId, decimal amount, string currency, PaymentProvider provider, string? payload = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var operation = new PaymentOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = provider,
                Amount = amount,
                Currency = currency,
                Status = PaymentOperationStatus.Pending,
                RequestedAt = DateTime.UtcNow,
                Payload = payload
            };

            _dbContext.PaymentOperations.Add(operation);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Registered payment operation {OperationId} for user {UserId}", operation.Id, userId);

            return operation;
        }

        public async Task<PaymentOperation?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
        {
            if (operationId == Guid.Empty)
            {
                return null;
            }

            return await _dbContext.PaymentOperations
                .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<PaymentOperation> MarkSucceededAsync(Guid operationId, string externalOperationId, Guid? walletTransactionId = null, CancellationToken cancellationToken = default)
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken).ConfigureAwait(false);

            operation.Status = PaymentOperationStatus.Succeeded;
            operation.ExternalOperationId = externalOperationId;
            operation.CompletedAt = DateTime.UtcNow;
            operation.WalletTransactionId = walletTransactionId;

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Payment operation {OperationId} completed successfully", operationId);

            return operation;
        }

        public async Task<PaymentOperation> MarkFailedAsync(Guid operationId, string? payload = null, CancellationToken cancellationToken = default)
        {
            var operation = await RequireOperationAsync(operationId, cancellationToken).ConfigureAwait(false);

            operation.Status = PaymentOperationStatus.Failed;
            operation.Payload = payload;
            operation.CompletedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogWarning("Payment operation {OperationId} marked as failed", operationId);

            return operation;
        }

        private async Task<PaymentOperation> RequireOperationAsync(Guid operationId, CancellationToken cancellationToken)
        {
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation identifier is required.", nameof(operationId));
            }

            return await _dbContext.PaymentOperations
                .FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Payment operation '{operationId}' was not found.");
        }
    }
}
