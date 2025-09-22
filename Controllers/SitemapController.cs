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

            // Извлекаем все необходимые Id из таблицы YoutubeCaptionTasks
            var ids = await _dbContext.YoutubeCaptionTasks
                .Where(t => t.Status == RecognizeStatus.Done) // Например, только завершенные задачи
                .Select(t => t.Slug)
                .ToListAsync();

            // Формируем список URL
            var urls = ids.Select(id => $"{baseUrl}/recognized/{id}").ToList();

            // Создаем XML документ
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            var sitemap = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ns + "urlset",
                    from url in urls
                    select new XElement(ns + "url",
                        new XElement(ns + "loc", url),
                        new XElement(ns + "lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd")),
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
