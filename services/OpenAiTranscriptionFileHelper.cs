using System;
using System.IO;
using System.Linq;

namespace YandexSpeech.services
{
    internal static class OpenAiTranscriptionFileHelper
    {
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
    }
}
