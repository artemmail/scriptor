using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.services;

namespace YandexSpeech.services.Interface
{
    public interface IYooMoneyRepository
    {
        Task<OperationDetails?> GetOperationDetailsAsync(string operationId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryAsync(int from, int count, CancellationToken cancellationToken = default);

        Task<string> AuthorizeAsync(CancellationToken cancellationToken = default);

        Task<string> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default);

        Task<BillDetails?> GetBillDetailsAsync(string billId, CancellationToken cancellationToken = default);
    }
}
