using System.Threading;
using System.Threading.Tasks;
using YandexSpeech.services.Whisper;

namespace YandexSpeech.services
{
    public interface IWhisperTranscriptionService
    {
        Task<WhisperTranscriptionResult> TranscribeAsync(
            string audioFilePath,
            string workingDirectory,
            string? ffmpegExecutable,
            CancellationToken cancellationToken = default);
    }
}
