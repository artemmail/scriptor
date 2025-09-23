using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public interface IOpenAiTranscriptionService
    {
        Task<OpenAiTranscriptionTask> StartTranscriptionAsync(string sourceFilePath, string createdBy);
        Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId);
    }
}
