using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YandexSpeech.models.DB
{
    public enum OpenAiTranscriptionStatus
    {
        Created = 0,
        Downloading = 5,
        Converting = 10,
        Transcribing = 20,
        Segmenting = 25,
        ProcessingSegments = 27,
        Formatting = 30,
        Done = 900,
        Error = 999
    }

    public enum OpenAiTranscriptionStepStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Error = 3
    }

    [Table("OpenAiTranscriptionTasks")]
    public class OpenAiTranscriptionTask
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string SourceFilePath { get; set; } = null!;

        public string? SourceFileUrl { get; set; }

        public string? ConvertedFilePath { get; set; }

        public string? RecognizedText { get; set; }

        public string? SegmentsJson { get; set; }

        public string? ProcessedText { get; set; }

        public string? MarkdownText { get; set; }

        public string? Clarification { get; set; }

        public OpenAiTranscriptionStatus Status { get; set; } = OpenAiTranscriptionStatus.Created;

        public bool Done { get; set; }

        public string? Error { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string CreatedBy { get; set; } = null!;

        public int SegmentsTotal { get; set; }

        public int SegmentsProcessed { get; set; }

        public virtual ICollection<OpenAiTranscriptionStep> Steps { get; set; }
            = new List<OpenAiTranscriptionStep>();

        public virtual ICollection<OpenAiRecognizedSegment> Segments { get; set; }
            = new List<OpenAiRecognizedSegment>();
    }

    [Table("OpenAiTranscriptionSteps")]
    public class OpenAiTranscriptionStep
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(Task))]
        public string TaskId { get; set; } = null!;

        public virtual OpenAiTranscriptionTask Task { get; set; } = null!;

        [Required]
        public OpenAiTranscriptionStatus Step { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? FinishedAt { get; set; }

        public OpenAiTranscriptionStepStatus Status { get; set; }

        public string? Error { get; set; }
    }
}
