using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Interface
{
    public interface IPaymentGatewayService
    {
        Task<PaymentOperation> RegisterOperationAsync(string userId, decimal amount, string currency, PaymentProvider provider, string? payload = null, CancellationToken cancellationToken = default);

        Task<PaymentOperation?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default);

        Task<PaymentOperation> MarkSucceededAsync(Guid operationId, string externalOperationId, Guid? walletTransactionId = null, CancellationToken cancellationToken = default);

        Task<PaymentOperation> MarkFailedAsync(Guid operationId, string? payload = null, CancellationToken cancellationToken = default);
    }
}
