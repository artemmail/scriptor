using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services.Interface
{
    public interface IWalletService
    {
        Task<UserWallet> GetOrCreateWalletAsync(string userId, CancellationToken cancellationToken = default);

        Task<WalletTransaction> DepositAsync(string userId, decimal amount, string currency, Guid? relatedEntityId = null, string? reference = null, string? comment = null, CancellationToken cancellationToken = default);

        Task<WalletTransaction> DebitAsync(string userId, decimal amount, string currency, Guid? relatedEntityId = null, string? reference = null, string? comment = null, CancellationToken cancellationToken = default);

        Task<WalletTransaction> RefundAsync(Guid transactionId, decimal amount, string? comment = null, CancellationToken cancellationToken = default);

        Task<decimal> GetBalanceAsync(string userId, CancellationToken cancellationToken = default);
    }
}
