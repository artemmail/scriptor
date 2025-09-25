using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace YandexSpeech.services.Whisper
{
    internal static class WhisperTranscriptionHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static WhisperTranscriptionResponse? Parse(string json)
        {
            return JsonSerializer.Deserialize<WhisperTranscriptionResponse>(json, JsonOptions);
        }

        public static string BuildTimecodedText(WhisperTranscriptionResponse parsed)
        {
            if (parsed.Segments.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var segment in parsed.Segments)
            {
                var start = TimeSpan.FromSeconds(segment.Start);
                var end = TimeSpan.FromSeconds(segment.End);
                var text = (segment.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                builder.Append('[')
                    .Append(FormatTimestamp(start))
                    .Append(" --> ")
                    .Append(FormatTimestamp(end))
                    .Append("] ")
                    .AppendLine(text);
            }

            return builder.ToString().Trim();
        }

        public static string? FindFirstJsonFile(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.json").FirstOrDefault()
                : null;
        }

        private static string FormatTimestamp(TimeSpan time) => time.ToString(@"hh\:mm\:ss\.fff");
    }
}
