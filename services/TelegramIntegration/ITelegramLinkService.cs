using System;
using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO.Telegram;

namespace YandexSpeech.services.TelegramIntegration
{
    public interface ITelegramLinkService
    {
        Task<TelegramLinkInitiationResult> CreateLinkTokenAsync(
            TelegramLinkInitiationContext context,
            CancellationToken cancellationToken = default);

        Task<TelegramLinkConfirmationResult> ConfirmLinkAsync(
            string token,
            string userId,
            CancellationToken cancellationToken = default);

        Task<TelegramCalendarStatusDto> GetCalendarStatusAsync(
            long telegramId,
            CancellationToken cancellationToken = default);

        Task<TelegramCalendarStatusDto> RefreshCalendarStatusAsync(
            long telegramId,
            CancellationToken cancellationToken = default);

        Task<TelegramCalendarEventResult> CreateTestEventAsync(
            long telegramId,
            CancellationToken cancellationToken = default);

        Task<bool> UnlinkAsync(
            long telegramId,
            string? initiatedByUserId,
            CancellationToken cancellationToken = default);

        Task<TelegramAccountLink?> FindLinkByUserAsync(
            string userId,
            CancellationToken cancellationToken = default);
    }

    public sealed record TelegramLinkInitiationContext(
        long TelegramId,
        string? Username,
        string? FirstName,
        string? LastName,
        string? LanguageCode);
}
