using System;
using System.Collections.Generic;
using YandexSpeech.models.DB;

namespace YandexSpeech.models.DTO
{
    public class OpenAiTranscriptionTaskDto
    {
        public string Id { get; set; } = null!;

        public string FileName { get; set; } = null!;

        public string DisplayName { get; set; } = null!;

        public OpenAiTranscriptionStatus Status { get; set; }
            = OpenAiTranscriptionStatus.Created;

        public bool Done { get; set; }

        public string? Error { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime ModifiedAt { get; set; }

        public int SegmentsTotal { get; set; }

        public int SegmentsProcessed { get; set; }

        public string? Clarification { get; set; }

        public int? RecognitionProfileId { get; set; }

        public string? RecognitionProfileName { get; set; }

        public string? RecognitionProfileDisplayedName { get; set; }
    }

    public class OpenAiTranscriptionTaskDetailsDto : OpenAiTranscriptionTaskDto
    {
        public string? RecognizedText { get; set; }

        public string? ProcessedText { get; set; }

        public string? MarkdownText { get; set; }

        public bool HasSegments { get; set; }

        public IReadOnlyList<OpenAiTranscriptionStepDto> Steps { get; set; }
            = Array.Empty<OpenAiTranscriptionStepDto>();

        public IReadOnlyList<OpenAiRecognizedSegmentDto> Segments { get; set; }
            = Array.Empty<OpenAiRecognizedSegmentDto>();
    }

    public class OpenAiRecognizedSegmentDto
    {
        public int SegmentId { get; set; }

        public int Order { get; set; }

        public string Text { get; set; } = string.Empty;

        public string? ProcessedText { get; set; }

        public bool IsProcessed { get; set; }

        public bool IsProcessing { get; set; }

        public double? StartSeconds { get; set; }

        public double? EndSeconds { get; set; }
    }

    public class OpenAiTranscriptionStepDto
    {
        public int Id { get; set; }

        public OpenAiTranscriptionStatus Step { get; set; }

        public OpenAiTranscriptionStepStatus Status { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? FinishedAt { get; set; }

        public string? Error { get; set; }
    }
}
