// GenerateController.cs
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Threading.Tasks;

[ApiController]
[Route("api/generate")]
public class GenerateController : ControllerBase
{
    private readonly IDocumentGeneratorService _documentGeneratorService;

    public GenerateController(IDocumentGeneratorService documentGeneratorService)
    {
        _documentGeneratorService = documentGeneratorService;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> GeneratePdf([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return BadRequest("Markdown content is required.");
        }

        try
        {
            var pdfPath = await _documentGeneratorService.GeneratePdfFromMarkdownAsync(request.Id ?? "temp", request.Markdown);
            var bytes = await System.IO.File.ReadAllBytesAsync(pdfPath);

            // Опционально: удалить временный файл после чтения
            System.IO.File.Delete(pdfPath);

            return File(bytes, "application/pdf", $"{request.Id ?? "converted"}.pdf");
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            Console.Error.WriteLine(ex);
            return StatusCode(500, "Error generating PDF.");
        }
    }

    [HttpPost("bbcode")]
    public async Task<IActionResult> GenerateBbcode([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return BadRequest("Markdown content is required.");
        }

        try
        {
            var bbcode = await _documentGeneratorService.GenerateBbcodeFromMarkdownAsync(request.Id ?? "temp", request.Markdown);
            var bytes = Encoding.UTF8.GetBytes(bbcode);

            return File(bytes, "text/plain", $"{request.Id ?? "converted"}.bbcode");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return StatusCode(500, "Error generating BBCode.");
        }
    }

    [HttpPost("docx")]
    public async Task<IActionResult> GenerateWord([FromBody] GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return BadRequest("Markdown content is required.");
        }

        try
        {
            var docxPath = await _documentGeneratorService.GenerateWordFromMarkdownAsync(request.Id ?? "temp", request.Markdown);
            var bytes = await System.IO.File.ReadAllBytesAsync(docxPath);

            // Опционально: удалить временный файл после чтения
            System.IO.File.Delete(docxPath);

            return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{request.Id ?? "converted"}.docx");
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            Console.Error.WriteLine(ex);
            return StatusCode(500, "Error generating Word document.");
        }
    }
}

public class GenerateRequest
{
    public string? Id { get; set; } // Опциональный, для имени файла
    public string Markdown { get; set; } = string.Empty;
}