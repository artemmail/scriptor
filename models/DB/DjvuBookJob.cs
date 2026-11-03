using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DB;

public sealed class DjvuBookJob
{
    [Key] public Guid Id { get; set; }
    [Required] public string UserId { get; set; } = "";
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(260)] public string FileName { get; set; } = "";
    [Required, MaxLength(32)] public string Status { get; set; } = "queued";
    public int TotalBlocks { get; set; }
    public int CompletedBlocks { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? CreditChargedAt { get; set; }
    [MaxLength(1000)] public string? Error { get; set; }
    public string? ResultXml { get; set; }
    public List<DjvuBookChapter> Chapters { get; set; } = [];
    public List<DjvuBookBlock> Blocks { get; set; } = [];
}

public sealed class DjvuBookChapter
{
    [Key] public long Id { get; set; }
    public Guid JobId { get; set; }
    public int Ordinal { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    public int Depth { get; set; }
    public int Page { get; set; }
    public DjvuBookJob Job { get; set; } = null!;
}

public sealed class DjvuBookBlock
{
    [Key] public long Id { get; set; }
    public Guid JobId { get; set; }
    public int Ordinal { get; set; }
    public int ChapterIndex { get; set; }
    [Required] public string Text { get; set; } = "";
    public string? Fragment { get; set; }
    public DjvuBookJob Job { get; set; } = null!;
}
