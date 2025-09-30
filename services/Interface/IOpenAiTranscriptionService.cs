using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public interface IOpenAiTranscriptionService
    {
        Task<OpenAiTranscriptionTask> StartTranscriptionAsync(
            string sourceFilePath,
            string createdBy,
            string? clarification = null,
            string? sourceFileUrl = null);
        Task<OpenAiTranscriptionTask?> PrepareForContinuationAsync(string taskId);
        Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId);
    }
}
