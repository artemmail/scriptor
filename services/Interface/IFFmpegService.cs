using System.Threading;
using System.Threading.Tasks;

namespace YandexSpeech.services.Interface
{
    public interface IFfmpegService
    {
        Task ConvertToWav16kMonoAsync(
            string sourcePath,
            string outputPath,
            CancellationToken cancellationToken = default,
            string? overrideExecutable = null);

        string ResolveFfmpegExecutable(string? overrideExecutable = null);

        string? ResolveFfmpegDirectory(string? overrideExecutable = null);
    }
}
