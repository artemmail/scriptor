using System.ComponentModel.DataAnnotations;

namespace YandexSpeech.models.DTO;

public sealed class SupportFormModel
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public const long MaxRequestSize = MaxFileSize + 1024 * 1024;

    [Required, StringLength(100), RegularExpression(@"^[^\r\n]+$")]
    public string MessageType { get; set; } = string.Empty;

    [Required, StringLength(200), RegularExpression(@"^[^\r\n]+$")]
    public string Header { get; set; } = string.Empty;

    [Required, StringLength(10000)]
    public string Text { get; set; } = string.Empty;

    public IFormFile? UploadedFile { get; set; }
}
