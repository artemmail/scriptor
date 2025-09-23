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
    }

    public class OpenAiTranscriptionTaskDetailsDto : OpenAiTranscriptionTaskDto
    {
        public string? RecognizedText { get; set; }

        public string? MarkdownText { get; set; }

        public IReadOnlyList<OpenAiTranscriptionStepDto> Steps { get; set; }
            = Array.Empty<OpenAiTranscriptionStepDto>();
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
