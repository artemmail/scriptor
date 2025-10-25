using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.models.DTO.Telegram;

namespace YandexSpeech.services.TelegramIntegration
{
    public interface ITelegramIntegrationNotifier
    {
        Task NotifyStatusAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default);

        Task NotifyLinkCompletedAsync(long telegramId, TelegramCalendarStatusDto status, CancellationToken cancellationToken = default);

        Task NotifyLinkFailedAsync(long telegramId, string reason, CancellationToken cancellationToken = default);
    }
}
