using System.Threading;
using System.Threading.Tasks;

namespace YandexSpeech.services.Interface
{
    public interface IYooMoneyAutoActivationService
    {
        Task<int> ProcessAsync(CancellationToken cancellationToken = default);
    }
}
