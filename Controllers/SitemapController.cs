using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml.Linq;
using YandexSpeech.models.DB; // Ваши модели
using Microsoft.EntityFrameworkCore;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SitemapController : ControllerBase
    {
        private readonly MyDbContext _dbContext;

        public SitemapController(MyDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Обработка запроса к /robots.txt
        [HttpGet("/robots.txt")]
        public IActionResult GetRobotsTxt()
        {
            // Получаем базовый URL из запроса
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Формируем содержимое robots.txt:
            // Разрешаем индексировать все страницы и указываем путь к sitemap.xml
            string content = $"User-agent: *{Environment.NewLine}" +
                             $"Allow: /{Environment.NewLine}{Environment.NewLine}" +
                             $"Sitemap: {baseUrl}/sitemap.xml";

            return Content(content, "text/plain", Encoding.UTF8);
        }

        [HttpGet("/sitemap.xml")]
        public async Task<IActionResult> GetSitemap()
        {
            // Получаем базовый URL из запроса
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Извлекаем все необходимые Slug из таблицы YoutubeCaptionTasks
            var tasks = await _dbContext.YoutubeCaptionTasks
                .Where(t => t.Status == RecognizeStatus.Done && !string.IsNullOrEmpty(t.Slug)) // Например, только завершенные задачи
                .Select(t => new { t.Slug, t.ModifiedAt, t.CreatedAt })
                .ToListAsync();

            // Извлекаем опубликованные темы блога
            var blogTopics = await _dbContext.BlogTopics
                .Select(t => new { t.Slug, t.CreatedAt })
                .ToListAsync();

            // Формируем список URL
            var urls = tasks.Select(task => new
            {
                Url = $"{baseUrl}/recognized/{task.Slug}",
                LastModified = task.ModifiedAt ?? task.CreatedAt ?? DateTime.UtcNow
            }).ToList();

            urls.AddRange(blogTopics.Select(topic => new
            {
                Url = $"{baseUrl}/blog/{topic.Slug}",
                LastModified = topic.CreatedAt
            }));

            // Создаем XML документ
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var sitemap = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ns + "urlset",
                    from url in urls
                    select new XElement(ns + "url",
                        new XElement(ns + "loc", url.Url),
                        new XElement(ns + "lastmod", url.LastModified.ToString("yyyy-MM-dd")),
                        new XElement(ns + "changefreq", "weekly"),
                        new XElement(ns + "priority", "0.8")
                    )
                )
            );

            // Возвращаем XML с правильным типом контента
            return Content(sitemap.ToString(), "application/xml", Encoding.UTF8);
        }
    }
}
