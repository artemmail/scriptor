using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Interface
{
    public interface IPaymentOperationApplicationService
    {
        Task<PaymentOperation?> ApplyAsync(
            Guid operationId,
            string? reference = null,
            string? externalPayload = null,
            CancellationToken cancellationToken = default);
    }
}
