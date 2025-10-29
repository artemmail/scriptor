using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YandexSpeech.services
{
    internal static class OpenAiTranscriptionFileHelper
    {
        public const long HtmlDetectionMaxFileSize = 2 * 1024 * 1024;
        public const string HtmlFileNotSupportedMessage = "вы пытаетесь загрузить html файл, необходимо аудио или видео";

        public static string GenerateStoredFilePath(string uploadsDirectory, string sanitizedName)
        {
            var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}__{sanitizedName}";
            return Path.Combine(uploadsDirectory, storedFileName);
        }

        public static string SanitizeFileName(string? fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string((fileName ?? string.Empty).Where(c => !invalidChars.Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = "audio";
            }

            var extension = Path.GetExtension(cleaned);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(cleaned);

            if (nameWithoutExtension.Length > 80)
            {
                nameWithoutExtension = nameWithoutExtension[..80];
            }

            return string.IsNullOrEmpty(extension)
                ? nameWithoutExtension
                : $"{nameWithoutExtension}{extension}";
        }

        public static async Task<bool> ContainsHtmlAsync(
            Stream stream,
            long length,
            CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (length > HtmlDetectionMaxFileSize)
            {
                if (stream.CanSeek)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }

                return false;
            }

            return await HtmlFileDetector
                .IsHtmlAsync(stream, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
