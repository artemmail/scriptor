using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    /// <summary>
    /// Сервис управления полным циклом распознавания аудио:
    /// от загрузки/конвертации до сегментации и финального текста.
    /// </summary>
    public interface ISpeechWorkflowService
    {
        Task<AudioWorkflowTask> StartRecognitionTaskAsync(string fileId, string createdBy);
        Task ContinueRecognitionAsync(string taskId);
    }

}
