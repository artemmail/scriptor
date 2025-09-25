using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    [Table("RecognitionUsage")]
    public class RecognitionUsage
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Column(TypeName = "date")]
        public DateTime Date { get; set; } = DateTime.UtcNow.Date;

        public int RecognitionsCount { get; set; }

        public int? TokensUsed { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ChargedAmount { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "RUB";

        public Guid? WalletTransactionId { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
