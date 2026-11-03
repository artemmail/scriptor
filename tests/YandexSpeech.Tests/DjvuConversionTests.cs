using System;
using Xunit;
using YandexSpeech.Controllers;

namespace YandexSpeech.Tests;

public class DjvuConversionTests
{
    [Fact]
    public void AcceptsOnlyValidFb2TextMarkup()
    {
        var xml = DjvuConversionController.NormalizeFragment(
            "```xml\n<p>Первая <strong>важная</strong> строка.</p><subtitle>Подзаголовок</subtitle>\n```");

        Assert.Equal("<p>Первая <strong>важная</strong> строка.</p><subtitle>Подзаголовок</subtitle>", xml);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<!ENTITY x SYSTEM \"file:///etc/passwd\"><p>&x;</p>")]
    public void RejectsUnsupportedOrActiveMarkup(string fragment)
    {
        Assert.ThrowsAny<Exception>(() => DjvuConversionController.NormalizeFragment(fragment));
    }

    [Fact]
    public void NormalizesCommonModelMarkupWithoutLosingText()
    {
        var xml = DjvuConversionController.NormalizeFragment(
            "<section><title><p>Глава 1</p></title><p class=\"ocr\">Это <b>важно</b><br>и <i>интересно</i>&nbsp;для читателя.</p><p>Текст <a href=\"#n1\">сноски</a>.</p></section>",
            "Глава 1");

        Assert.Equal("<p>Это <strong>важно</strong> и <emphasis>интересно</emphasis> для читателя.</p><p>Текст сноски.</p>", xml);
    }
}
