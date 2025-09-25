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
    public class WalletService : IWalletService
    {
        private readonly MyDbContext _dbContext;
        private readonly ILogger<WalletService> _logger;

        public WalletService(MyDbContext dbContext, ILogger<WalletService> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserWallet> GetOrCreateWalletAsync(string userId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var wallet = await _dbContext.UserWallets
                .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (wallet != null)
            {
                return wallet;
            }

            wallet = new UserWallet
            {
                UserId = userId,
                Balance = 0,
                Currency = "RUB",
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.UserWallets.Add(wallet);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created wallet for user {UserId}", userId);

            return wallet;
        }

        public async Task<WalletTransaction> DepositAsync(string userId, decimal amount, string currency, Guid? relatedEntityId = null, string? reference = null, string? comment = null, CancellationToken cancellationToken = default)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            var wallet = await GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);

            wallet.Balance += amount;
            wallet.Currency = currency;
            wallet.UpdatedAt = DateTime.UtcNow;

            var transaction = CreateTransaction(userId, WalletTransactionType.Deposit, amount, currency, relatedEntityId, reference, comment);

            _dbContext.WalletTransactions.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Deposited {Amount} {Currency} to user {UserId}", amount, currency, userId);

            return transaction;
        }

        public async Task<WalletTransaction> DebitAsync(string userId, decimal amount, string currency, Guid? relatedEntityId = null, string? reference = null, string? comment = null, CancellationToken cancellationToken = default)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            var wallet = await GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);

            if (wallet.Currency != currency && wallet.Balance > 0)
            {
                throw new InvalidOperationException("Currency mismatch between wallet and debit request.");
            }

            if (wallet.Balance < amount)
            {
                throw new InvalidOperationException("Insufficient wallet balance.");
            }

            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            var transaction = CreateTransaction(userId, WalletTransactionType.Debit, -amount, currency, relatedEntityId, reference, comment);

            _dbContext.WalletTransactions.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Debited {Amount} {Currency} from user {UserId}", amount, currency, userId);

            return transaction;
        }

        public async Task<WalletTransaction> RefundAsync(Guid transactionId, decimal amount, string? comment = null, CancellationToken cancellationToken = default)
        {
            if (transactionId == Guid.Empty)
            {
                throw new ArgumentException("Transaction identifier is required.", nameof(transactionId));
            }

            var original = await _dbContext.WalletTransactions
                .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Original transaction was not found.");

            var currency = original.Currency;
            var userId = original.UserId;

            return await DepositAsync(userId, amount, currency, transactionId, original.Reference, comment, cancellationToken).ConfigureAwait(false);
        }

        public async Task<decimal> GetBalanceAsync(string userId, CancellationToken cancellationToken = default)
        {
            var wallet = await GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);
            return wallet.Balance;
        }

        private static WalletTransaction CreateTransaction(string userId, WalletTransactionType type, decimal amount, string currency, Guid? relatedEntityId, string? reference, string? comment)
        {
            return new WalletTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = type,
                Amount = amount,
                Currency = currency,
                RelatedEntityId = relatedEntityId,
                Reference = reference,
                Comment = comment,
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
