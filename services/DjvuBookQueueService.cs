using System.Data;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services;

public sealed class DjvuBookQueueService(MyDbContext db, OpenAiBatchClient ai, IConfiguration config,
    ISubscriptionService subscriptions, ILogger<DjvuBookQueueService> logger)
{
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

    public async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await db.DjvuBookJobs
            .Where(j => j.Status == "queued" || j.Status == "processing")
            .OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (job is null) return;
        job.Status = "processing";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var blocks = await db.DjvuBookBlocks.Where(b => b.JobId == job.Id).OrderBy(b => b.Ordinal).ToListAsync(cancellationToken);
        var chapters = await db.DjvuBookChapters.Where(c => c.JobId == job.Id).OrderBy(c => c.Ordinal).ToListAsync(cancellationToken);
        try
        {
            foreach (var block in blocks.Where(b => b.Fragment is null))
            {
                var chapter = chapters[block.ChapterIndex];
                var previous = blocks.Where(b => b.Ordinal < block.Ordinal && b.ChapterIndex == block.ChapterIndex && b.Fragment is not null)
                    .LastOrDefault()?.Fragment;
                var context = previous is null ? "" : XElement.Parse("<root>" + previous + "</root>").Value;
                if (context.Length > 700) context = context[^700..];
                block.Fragment = await ConvertBlockWithRetryAsync(job.Id, block.Text, chapter.Title, context, cancellationToken);
                job.CompletedBlocks++;
                job.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var xml = BuildFb2(job.Title, chapters, blocks);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var balance = await subscriptions.GetQuotaBalanceAsync(job.UserId, cancellationToken);
            if (balance.RemainingVideos < 10 ||
                !await subscriptions.TryConsumeQuotaAsync(job.UserId, 0, 10, $"djvu-book:{job.Id}", cancellationToken))
            {
                job.Status = "awaiting_credits";
                job.Error = "Для получения книги нужно 10 видео-кредитов. Пополните баланс и нажмите «Продолжить».";
            }
            else
            {
                job.ResultXml = xml;
                job.CreditChargedAt = DateTime.UtcNow;
                job.CompletedAt = DateTime.UtcNow;
                job.Status = "completed";
                job.Error = null;
            }
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "DjVu book job {JobId} failed", job.Id);
            job.Status = "failed";
            job.Error = "Не удалось обработать блок книги. Нажмите «Повторить»: готовые блоки сохранятся.";
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<string> ConvertBlockWithRetryAsync(Guid jobId, string text, string chapterTitle,
        string context, CancellationToken cancellationToken)
    {
        var model = config["OpenAI:Model"] ?? throw new InvalidOperationException("OpenAI:Model is not configured.");
        var prompt = $"Глава: {chapterTitle}\nПредыдущий обработанный контекст (не повторять):\n{context}\n\nНОВЫЙ ТЕКСТ:\n{text}";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await ai.CompleteDirectAsync(new
                {
                    model,
                    messages = new object[] { new { role = "system", content = Instructions }, new { role = "user", content = prompt } }
                }, cancellationToken);
                var fragment = DjvuConversionController.NormalizeFragment(response, chapterTitle);
                if (text.Length > 500 && XElement.Parse("<root>" + fragment + "</root>").Value.Length < text.Length / 4)
                    throw new InvalidOperationException("The model returned too little text.");
                return fragment;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (attempt < 3)
            {
                logger.LogWarning(error, "DjVu book {JobId} block attempt {Attempt} failed; retrying", jobId, attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 3), cancellationToken);
            }
        }
    }

    public static string BuildFb2(string title, IReadOnlyList<DjvuBookChapter> chapters, IReadOnlyList<DjvuBookBlock> blocks)
    {
        XNamespace ns = "http://www.gribuser.ru/xml/fictionbook/2.0";
        var body = new XElement(ns + "body");
        var stack = new List<(int depth, XElement section)>();
        foreach (var chapter in chapters.OrderBy(c => c.Ordinal))
        {
            var section = new XElement(ns + "section", new XElement(ns + "title", new XElement(ns + "p", chapter.Title)));
            foreach (var block in blocks.Where(b => b.ChapterIndex == chapter.Ordinal).OrderBy(b => b.Ordinal))
            {
                var root = XElement.Parse("<root>" + block.Fragment + "</root>");
                foreach (var element in root.Elements()) section.Add(ToFb2Namespace(element, ns));
            }
            while (stack.Count > 0 && stack[^1].depth >= chapter.Depth) stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) body.Add(section); else stack[^1].section.Add(section);
            stack.Add((chapter.Depth, section));
        }
        var language = Regex.IsMatch(title + string.Concat(chapters.Select(c => c.Title)), "[А-Яа-яЁё]") ? "ru" : "en";
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "FictionBook", new XElement(ns + "description",
                new XElement(ns + "title-info", new XElement(ns + "genre", "other"),
                    new XElement(ns + "book-title", title), new XElement(ns + "lang", language))), body));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document.Root!.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ToFb2Namespace(XElement element, XNamespace ns) =>
        new(ns + element.Name.LocalName, element.Nodes().Select(node => node is XElement child
            ? (object)ToFb2Namespace(child, ns) : node is XText text ? new XText(text.Value) : new XText("")));
}

public sealed class DjvuBookBackgroundService(IServiceScopeFactory scopes, ILogger<DjvuBookBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DjvuBookQueueService>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "DjVu book queue iteration failed"); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
