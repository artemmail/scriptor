using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Token { get; set; }

        // Идентификатор пользователя (строковый, т.к. IdentityUser использует string)
        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        // Дата создания токена
        public DateTime Created { get; set; }

        // Срок истечения токена
        public DateTime Expires { get; set; }

        // Флаг отзыва токена
        public bool IsRevoked { get; set; }
    }
}
