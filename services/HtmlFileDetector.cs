using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YandexSpeech.services
{
    internal static class HtmlFileDetector
    {
        private const int SampleSize = 512;

        public static async Task<bool> IsHtmlAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            var buffer = new byte[SampleSize];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);

            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            if (bytesRead <= 0)
            {
                return false;
            }

            var textSample = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var trimmed = TrimLeading(textSample);

            if (trimmed.Length == 0)
            {
                return false;
            }

            var prefixLength = Math.Min(128, trimmed.Length);
            var lowered = trimmed[..prefixLength].ToLowerInvariant();

            return lowered.StartsWith("<!doctype html") || lowered.StartsWith("<html");
        }

        private static string TrimLeading(string value)
        {
            return value.TrimStart('\uFEFF', '\u0000', '\t', '\r', '\n', ' ');
        }
    }
}
