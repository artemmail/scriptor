using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YandexSpeech.Extensions;
using YandexSpeech.services;

namespace YandexSpeech.Controllers;

[ApiController]
[Route("api/djvu-conversion")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DjvuConversionController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
        { "p", "subtitle", "empty-line" };
    private static readonly HashSet<string> InlineElements = new(StringComparer.Ordinal)
        { "strong", "emphasis", "strikethrough", "sup", "sub", "code" };
    private const string Instructions = """
        Ты аккуратный редактор текста книги, извлечённого из текстового слоя DjVu.
        Обработай только НОВЫЙ ТЕКСТ. Склей строки и абзацы, разорванные при сканировании, исправь очевидные OCR-ошибки,
        убери повторяющиеся колонтитулы, отдельные номера страниц и бессмысленные маркеры сносок.
        Содержательные примечания сохрани обычными абзацами. Не сокращай, не пересказывай и не придумывай текст.
        Метки [Страница N] только обозначают границы страниц; никогда не выводи их в результате.
        Предыдущий обработанный фрагмент дан только для согласования границы; не повторяй его в ответе.
        Заголовок текущей главы будет добавлен отдельно по оглавлению, не повторяй его в ответе.
        Верни ТОЛЬКО XML-фрагмент для FB2 без обёртки, markdown и пояснений.
        На верхнем уровне разрешены <p>, <subtitle> и <empty-line/>.
        Внутри <p> и <subtitle> разрешены обычный текст, <strong>, <emphasis>, <strikethrough>, <sup>, <sub>, <code>.
        Применяй жирный и курсив только там, где это явно следует из смысла или исходного оформления.
        Экранируй &, < и > в тексте. Не используй атрибуты, ссылки, сущности или другие теги.
        """;

    private readonly OpenAiBatchClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DjvuConversionController> _logger;

    public DjvuConversionController(OpenAiBatchClient client, IConfiguration configuration, ILogger<DjvuConversionController> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("process-block")]
    [RequestSizeLimit(32_000)]
    public async Task<ActionResult<DjvuConversionBlockResponse>> ProcessBlock(
        [FromBody] DjvuConversionBlockRequest request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return StatusCode(410, new { error = "Конвертация книг перенесена в очередь «Мои книги»." });
    }

    public static string NormalizeFragment(string response, string? chapterTitle = null)
    {
        var fragment = response.Trim();
        if (fragment.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = fragment.IndexOf('\n');
            var closing = fragment.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && closing > firstLine)
                fragment = fragment[(firstLine + 1)..closing].Trim();
        }

        if (fragment.Contains("<!", StringComparison.Ordinal) || fragment.Contains("<?", StringComparison.Ordinal))
            throw new InvalidOperationException("Model returned prohibited XML declarations.");

        fragment = Regex.Replace(fragment, "<br\\s*>", "<br/>", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, "<hr\\s*>", "<empty-line/>", RegexOptions.IgnoreCase);
        fragment = Regex.Replace(fragment, "&([A-Za-z][A-Za-z0-9]+);", match =>
            match.Groups[1].Value.ToLowerInvariant() switch
            {
                "nbsp" => " ",
                "mdash" => "—",
                "ndash" => "–",
                "laquo" => "«",
                "raquo" => "»",
                "hellip" => "…",
                "amp" or "lt" or "gt" or "quot" or "apos" => match.Value,
                _ => "&amp;" + match.Groups[1].Value + ";"
            });
        fragment = Regex.Replace(fragment, "&(?!#(?:[0-9]+|x[0-9a-fA-F]+);|(?:amp|lt|gt|quot|apos);)", "&amp;");
        var root = XElement.Parse("<root>" + fragment + "</root>", LoadOptions.PreserveWhitespace);
        var blocks = new List<XElement>();
        foreach (var node in root.Nodes()) AddBlocks(node, blocks, chapterTitle);
        foreach (var block in blocks) Validate(block, false);
        if (blocks.Count == 0 || string.IsNullOrWhiteSpace(string.Concat(blocks.Select(block => block.Value))))
            throw new InvalidOperationException("Model returned an empty FB2 fragment.");
        return string.Concat(blocks.Select(block => block.ToString(SaveOptions.DisableFormatting)));
    }

    private static void AddBlocks(XNode node, List<XElement> blocks, string? chapterTitle)
    {
        if (node is XText text)
        {
            if (!string.IsNullOrWhiteSpace(text.Value)) blocks.Add(new XElement("p", text.Value.Trim()));
            return;
        }
        if (node is not XElement element) return;
        var name = element.Name.LocalName.ToLowerInvariant();
        if (name is "script" or "style" or "iframe" or "object")
            throw new InvalidOperationException("Model returned active markup.");
        if (name is "empty-line" or "hr") { blocks.Add(new XElement("empty-line")); return; }
        if (name is "p" or "subtitle" or "title" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            var normalized = new XElement(name is "p" ? "p" : "subtitle", InlineNodes(element));
            if (!string.IsNullOrWhiteSpace(normalized.Value) && !SameHeading(normalized.Value, chapterTitle))
                blocks.Add(normalized);
            return;
        }
        if (element.Elements().Any())
        {
            foreach (var child in element.Nodes()) AddBlocks(child, blocks, chapterTitle);
        }
        else if (!string.IsNullOrWhiteSpace(element.Value))
        {
            blocks.Add(new XElement("p", element.Value.Trim()));
        }
    }

    private static IEnumerable<object> InlineNodes(XContainer container)
    {
        foreach (var node in container.Nodes())
        {
            if (node is XText text) { yield return new XText(text.Value); continue; }
            if (node is not XElement element) continue;
            var name = element.Name.LocalName.ToLowerInvariant();
            if (name is "script" or "style" or "iframe" or "object")
                throw new InvalidOperationException("Model returned active markup.");
            if (name is "br" or "empty-line") { yield return new XText(" "); continue; }
            var mapped = name switch
            {
                "b" or "strong" => "strong",
                "i" or "em" or "emphasis" => "emphasis",
                "s" or "del" or "strikethrough" => "strikethrough",
                "sup" or "sub" or "code" => name,
                _ => null
            };
            if (mapped is not null) yield return new XElement(mapped, InlineNodes(element));
            else foreach (var child in InlineNodes(element)) yield return child;
        }
    }

    private static bool SameHeading(string text, string? chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterTitle)) return false;
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant().Replace('ё', 'е'), "[^\\p{L}\\p{N}]", "");
        var title = Normalize(chapterTitle);
        return title.Length >= 4 && Normalize(text) == title;
    }

    private static void Validate(XElement element, bool inline)
    {
        if (element.HasAttributes || element.Name.NamespaceName.Length != 0 ||
            (inline ? !InlineElements.Contains(element.Name.LocalName) : !BlockElements.Contains(element.Name.LocalName)))
            throw new InvalidOperationException("Model returned unsupported FB2 markup.");
        if (element.Name.LocalName == "empty-line" && element.Nodes().Any())
            throw new InvalidOperationException("Empty line must have no content.");
        foreach (var child in element.Elements()) Validate(child, true);
    }
}

public sealed record DjvuConversionBlockRequest(string Text, string? ChapterTitle, string? PreviousContext);
public sealed record DjvuConversionBlockResponse(string Fragment);
