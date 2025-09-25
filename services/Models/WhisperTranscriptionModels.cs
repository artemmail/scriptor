using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YandexSpeech.services.Whisper
{
    public sealed class WhisperTranscriptionResult
    {
        public string TimecodedText { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
    }

    internal sealed class WhisperTranscriptionResponse
    {
        [JsonPropertyName("segments")]
        public List<WhisperSegment> Segments { get; set; } = new();
    }

    internal sealed class WhisperSegment
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("words")]
        public List<WhisperWord>? Words { get; set; }
    }

    internal sealed class WhisperWord
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }

        [JsonPropertyName("start")]
        public double? Start { get; set; }

        [JsonPropertyName("end")]
        public double? End { get; set; }
    }
}
