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
            string? recognitionProfileDisplayedName = null,
            int? sourceDurationSeconds = null,
            int requestedTranscriptionMinutes = 0);
        Task<OpenAiTranscriptionTask?> PrepareForContinuationAsync(string taskId);
        Task<OpenAiTranscriptionTask?> PrepareForContinuationFromSegmentAsync(string taskId, int segmentNumber);
        Task<OpenAiTranscriptionTask?> ContinueTranscriptionAsync(string taskId);
        Task<OpenAiTranscriptionTask> CloneForPostProcessingAsync(
            string taskId,
            string createdBy,
            int recognitionProfileId,
            string? clarification = null);
    }
}
