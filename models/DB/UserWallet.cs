using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum WalletTransactionType
    {
        Deposit = 0,
        Debit = 1,
        Refund = 2,
        Bonus = 3
    }

    [Table("UserWallets")]
    public class UserWallet
    {
        [Key]
        [ForeignKey(nameof(User))]
        public string UserId { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Balance { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser? User { get; set; }
    }

    [Table("WalletTransactions")]
    public class WalletTransaction
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public WalletTransactionType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? RelatedEntityId { get; set; }

        [MaxLength(128)]
        public string? Reference { get; set; }

        [MaxLength(512)]
        public string? Comment { get; set; }

        public string? Metadata { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
