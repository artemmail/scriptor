using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using System.Xml.Linq;

var cases = new (string Input, string? Chapter, string Expected)[]
{
    ("<p>Обычный <strong>текст</strong>.</p>", null, "<p>Обычный <strong>текст</strong>.</p>"),
    ("<section><title><p>Глава 1</p></title><p class=\"ocr\">Это <b>важно</b><br>и <i>интересно</i>&nbsp;для читателя.</p><p>Текст <a href=\"#n1\">сноски</a>.</p></section>", "Глава 1", "<p>Это <strong>важно</strong> и <emphasis>интересно</emphasis> для читателя.</p><p>Текст сноски.</p>"),
    ("```xml\n<p>Первая строка.</p>\n```", null, "<p>Первая строка.</p>")
};
foreach (var item in cases)
{
    var actual = DjvuConversionController.NormalizeFragment(item.Input, item.Chapter);
    if (actual != item.Expected) throw new Exception($"Unexpected FB2 fragment: {actual}");
}
try
{
    DjvuConversionController.NormalizeFragment("<script>alert(1)</script>");
    throw new Exception("Active markup was accepted.");
}
catch (InvalidOperationException) { }
Console.WriteLine("DjVu conversion normalization smoke tests passed.");

var chapters = new List<DjvuBookChapter>
{
    new() { Ordinal = 0, Title = "Часть первая", Depth = 0, Page = 1 },
    new() { Ordinal = 1, Title = "Глава 1", Depth = 1, Page = 2 },
    new() { Ordinal = 2, Title = "Глава 2", Depth = 1, Page = 3 }
};
var blocks = new List<DjvuBookBlock>
{
    new() { Ordinal = 0, ChapterIndex = 1, Text = "Исходный текст", Fragment = "<p>Текст <strong>главы</strong>.</p>" },
    new() { Ordinal = 1, ChapterIndex = 2, Text = "Второй исходный текст", Fragment = "<p>Вторая глава.</p>" }
};
var book = XDocument.Parse(DjvuBookQueueService.BuildFb2("Проверка", chapters, blocks));
XNamespace fb = "http://www.gribuser.ru/xml/fictionbook/2.0";
var sections = book.Root!.Element(fb + "body")!.Elements(fb + "section").ToList();
if (sections.Count != 1 || sections[0].Elements(fb + "section").Count() != 2 ||
    sections[0].Elements(fb + "section").First().Descendants(fb + "strong").Single().Value != "главы" ||
    book.Root.Element(fb + "description")?.Element(fb + "title-info")?.Element(fb + "lang")?.Value != "ru")
    throw new Exception("DjVu book FB2 structure is incorrect.");
Console.WriteLine("DjVu book assembly smoke tests passed.");
