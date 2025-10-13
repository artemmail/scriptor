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
            string? sourceFileUrl = null,
            int? recognitionProfileId = null,
            string? recognitionProfileDisplayedName = null);
        Task<OpenAiTranscriptionTask?> PrepareForContinuationAsync(string taskId);
        Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId);
        Task<OpenAiTranscriptionTask> CloneForPostProcessingAsync(
            string taskId,
            string createdBy,
            int recognitionProfileId,
            string? clarification = null);
    }
}
