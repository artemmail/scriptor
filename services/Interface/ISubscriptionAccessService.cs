using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.services.Models;

namespace YandexSpeech.services.Interface
{
    public interface ISubscriptionAccessService
    {
        Task<UsageDecision> AuthorizeYoutubeRecognitionAsync(
            string userId,
            int requestedVideos = 1,
            CancellationToken cancellationToken = default);

        Task<UsageDecision> AuthorizeTranscriptionAsync(
            string userId,
            int requestedTranscriptionMinutes,
            CancellationToken cancellationToken = default);
    }
}
