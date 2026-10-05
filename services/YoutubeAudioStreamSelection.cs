using System;
using System.Collections.Generic;
using System.Linq;
using YoutubeExplode.Videos.Streams;

namespace YandexSpeech.services;

public static class YoutubeAudioStreamSelection
{
    // URLs expire between listing and downloading. Match the selection against a fresh manifest.
    public static AudioOnlyStreamInfo Select(
        IEnumerable<AudioOnlyStreamInfo> streams,
        string? languageCode,
        string? language,
        string? container,
        string? codec,
        long? bitrate,
        long? size)
    {
        var candidates = streams
            .Where(s => string.IsNullOrWhiteSpace(container) || Same(s.Container.Name, container))
            .Where(s => string.IsNullOrWhiteSpace(codec) || Same(s.AudioCodec, codec))
            .Where(s => !bitrate.HasValue || s.Bitrate.BitsPerSecond == bitrate.Value)
            .Where(s => !size.HasValue || s.Size.Bytes == size.Value);

        if (!string.IsNullOrWhiteSpace(languageCode))
            candidates = candidates.Where(s => Same(s.AudioLanguage?.Code, languageCode));
        else if (!string.IsNullOrWhiteSpace(language))
            // Older cached lists and queued tasks contain the language name only.
            candidates = candidates.Where(s => Same(s.AudioLanguage?.Name, language)
                || Same(s.AudioLanguage?.Code, language));

        var matches = candidates.ToList();
        if (string.IsNullOrWhiteSpace(languageCode) && string.IsNullOrWhiteSpace(language)
            && matches.Select(s => s.AudioLanguage?.Code ?? s.AudioLanguage?.Name ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            throw new InvalidOperationException(
                "Язык аудиодорожки не указан. Найдите дорожки заново и выберите нужную озвучку.");

        return matches.FirstOrDefault() ?? throw new InvalidOperationException(
            $"Выбранная аудиодорожка ({language ?? languageCode ?? "без языка"}, {container}, {codec}, {bitrate} бит/с) больше недоступна. Найдите дорожки заново.");
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
