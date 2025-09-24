using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YoutubeDownload.Services;
using YoutubeExplode.Videos;

namespace YandexSpeech.services
{
    public class YSubtitlesService : IYSubtitlesService
    {
        private readonly MyDbContext _dbContext;

        // Можно использовать Dictionary как поле класса
        private readonly Dictionary<char, string> _translitMap = new Dictionary<char, string>
        {
            {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
            {'е', "e"}, {'ё', "yo"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"},
            {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"},
            {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"},
            {'у', "u"}, {'ф', "f"}, {'х', "kh"}, {'ц', "ts"}, {'ч', "ch"},
            {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""},  {'ы', "y"},  {'ь', ""},
            {'э', "e"}, {'ю', "yu"}, {'я', "ya"}
        };

        public YSubtitlesService(MyDbContext dbContext)
        {
            _dbContext = dbContext;
        }


        /*
        public async Task PopulateSlugsAsync()
        {
            var existingSlugs = _dbContext.Topics.Select(t => t.Slug).ToList();
            var topicsWithoutSlug = await _dbContext.Topics
                .Where(x => string.IsNullOrEmpty(x.Slug))
                .ToListAsync();

            foreach (var topic in topicsWithoutSlug)
            {
                topic.Slug = GenerateSlug(topic.Header, existingSlugs);
                _dbContext.Update(topic);
            }

            await _dbContext.SaveChangesAsync();
        }*/


        public async Task PopulateSlugsAsync()
        {
            var existingSlugs = _dbContext.YoutubeCaptionTasks.Select(t => t.Slug).ToList();
            var topicsWithoutSlug = await _dbContext.YoutubeCaptionTasks
                .Where(x => string.IsNullOrEmpty(x.Slug))
                .ToListAsync();

            foreach (var topic in topicsWithoutSlug)
            {
                topic.Slug = GenerateSlug(topic.Title, existingSlugs);
                _dbContext.Update(topic);
            }

            await _dbContext.SaveChangesAsync();
        }

        // -----------------------------
        // Утилитные методы
        // -----------------------------
        public string Transliterate(string text)
        {
            text = text.Trim().ToLower();
            var result = new StringBuilder();
            foreach (var c in text)
            {
                if (_translitMap.ContainsKey(c))
                    result.Append(_translitMap[c]);
                else
                    result.Append(c);
            }
            return result.ToString();
        }

        public string RemoveMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            string text = markdown;

            // Удаление заголовков (например, # Заголовок)
            text = Regex.Replace(text, @"^(#{1,6})\s*", string.Empty, RegexOptions.Multiline);

            // Удаление жирного и курсивного текста (**...**, __...__, *...*, _..._)
            text = Regex.Replace(text, @"(\*\*|__)(.*?)\1", "$2");
            text = Regex.Replace(text, @"(\*|_)(.*?)\1", "$2");

            // Удаление ссылок [текст](url)
            text = Regex.Replace(text, @"\[(.*?)\]\(.*?\)", "$1");

            // Удаление изображений ![alt](url)
            text = Regex.Replace(text, @"!\[(.*?)\]\(.*?\)", "$1");

            // Удаление инлайн-кода `код`
            text = Regex.Replace(text, @"`([^`]+)`", "$1");

            // Удаление блоков кода ``` ... ```
            text = Regex.Replace(text, @"```[\s\S]*?```", string.Empty, RegexOptions.Singleline);

            // Удаление цитат > цитата
            text = Regex.Replace(text, @"^\s*>+\s?", string.Empty, RegexOptions.Multiline);

            // Удаление горизонтальных линий (---, ***, ___)
            text = Regex.Replace(text, @"^(-{3,}|\*{3,}|_{3,})$", string.Empty, RegexOptions.Multiline);

            // Удаление списков
            text = Regex.Replace(text, @"^\s*[-\*\+]\s+", string.Empty, RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*\d+\.\s+", string.Empty, RegexOptions.Multiline);

            // Удаление лишних пробелов
            text = Regex.Replace(text, @"\s{2,}", " ");

            // Финальная обработка
            return text.Trim();
        }


        public string GenerateSlug(string header)
        {
            var existingSlugs = _dbContext.YoutubeCaptionTasks.Select(t => t.Slug).ToList();
            var topicsWithoutSlug =  _dbContext.YoutubeCaptionTasks
                .Where(x => string.IsNullOrEmpty(x.Slug))
                .ToList();


            return GenerateSlug(header,existingSlugs);
        }


        public async Task<bool> NotifyYandexAsync(string domain, string key, string slug)
        {
            string baseUrl = "https://yandex.com/indexnow";
            string url = $"https://{domain}/Recognized/{slug}";
            

            // Формируем полный URL с параметрами
            string fullUrl = $"{baseUrl}?url={url}&key={key}";

            using (HttpClient client = new HttpClient())
            {

                try
                {
                    HttpResponseMessage response = await client.GetAsync(fullUrl);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    using (StreamWriter sw = System.IO.File.AppendText("c:/log/yandex.txt"))
                    {
                        sw.WriteLine(fullUrl);
                    }
                    // Обработка исключений при ошибке запроса
                    return false;
                }
            }
        }



        public string GenerateSlug(string header, IEnumerable<string> existingSlugs)
        {
            var transliterated = Transliterate(header);
            var slug = Regex.Replace(transliterated, @"[^a-zA-Z0-9\-]", "-").ToLower();

            string uniqueSlug = slug;
            int counter = 1;
            while (existingSlugs.Contains(uniqueSlug))
            {
                uniqueSlug = $"{slug}-{counter}";
                counter++;
            }

            return uniqueSlug;
        }

        // -----------------------------
        // Методы для работы с БД
        // -----------------------------
        public async Task<List<YoutubeCaptionTask>> GetAllTasksAsync()
        {
            return await _dbContext.YoutubeCaptionTasks.ToListAsync();
        }

        public async Task<YoutubeCaptionTask> GetTaskByIdAsync(string taskId)
        {
            return await _dbContext.YoutubeCaptionTasks
                .FirstOrDefaultAsync(t => t.Id == taskId);
        }



        public async Task<List<YoutubeCaptionTaskTableDto1>> GetAllTasksTableAsync()
        {
            // Выполняем запрос к базе с сортировкой по дате создания в обратном порядке
            // и выборкой только необходимых полей
            var itemsDto = await _dbContext.YoutubeCaptionTasks
                .Where(x=>x.Status==RecognizeStatus.Done)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new YoutubeCaptionTaskTableDto1
                {
                    ChannelName = t.ChannelName,
                    CreatedAt = t.CreatedAt ?? DateTime.Now,
                    UploadDate = t.UploadDate,
                    Title = t.Title,
                    Slug = t.Slug ?? t.Id
                })
                .ToListAsync();

            return itemsDto;
        }


        public async Task<(List<YoutubeCaptionTaskTableDto> Items, int TotalCount)> GetTasksPagedAsync(
            int page,
            int pageSize,
            string sortField,
            string sortOrder,
            string filter)
        {
            var query = _dbContext.YoutubeCaptionTasks
                .AsQueryable()
                .Where(x => x.ChannelId != "UCa0jIrHPmqCHopklH8ltmVw"); 

            // Фильтрация
            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(t =>
                    t.Title.Contains(filter) ||
                    t.ChannelName.Contains(filter)
                );
            }

            // Сортировка (через System.Linq.Dynamic.Core)
            if (!string.IsNullOrEmpty(sortField))
            {
                // Пример: "Title descending" или "Title ascending"
                var ordering = $"{sortField} {(sortOrder == "desc" ? "descending" : "ascending")}";
                try
                {
                 //   query = query.OrderBy(ordering);

                   // query = query.OrderByDescending(t => t.CreatedAt);
                }
                catch
                {
                    // Можно проигнорировать или бросить исключение
                }
            }
            else
            {
                // Сортировка по умолчанию — например, по дате создания в убывающем порядке
                query = query.OrderByDescending(t => t.CreatedAt);
            }

            // Подсчитываем общее число элементов
            int totalItems = await query.CountAsync();

            // Пагинация
            var tasks = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Формируем DTO
            var itemsDto = tasks.Select(t => new YoutubeCaptionTaskTableDto
            {
                Id = t.Id,
                ChannelId = t.ChannelId,
                ChannelName = t.ChannelName,
                UploadDate = t.UploadDate,
                Title = t.Title,
                Slug = t.Slug??t.Id,
                Error = t.Error,
                CreatedAt = t.CreatedAt ?? DateTime.Now,
                
                Status = t.Status,
                Done = t.Done,
                SegmentsProcessed = t.SegmentsProcessed,
                SegmentsTotal = t.SegmentsTotal,
                // Укороченный результат (используем RemoveMarkdown для превью)
                ResultShort = RemoveMarkdown(t.Preview),
            }).ToList();

            return (itemsDto, totalItems);
        }
    }
}
