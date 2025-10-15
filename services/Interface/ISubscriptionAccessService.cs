using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.services.Models;

namespace YandexSpeech.services.Interface
{
    public interface ISubscriptionAccessService
    {
        Task<UsageDecision> AuthorizeYoutubeRecognitionAsync(string userId, CancellationToken cancellationToken = default);

        Task<UsageDecision> AuthorizeTranscriptionAsync(string userId, CancellationToken cancellationToken = default);
    }
}
