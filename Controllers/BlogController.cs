using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlogController : ControllerBase
    {
        private static readonly IReadOnlyDictionary<char, string> TransliterationMap = new Dictionary<char, string>
        {
            ['а'] = "a",   ['б'] = "b",   ['в'] = "v",   ['г'] = "g",   ['д'] = "d",
            ['е'] = "e",   ['ё'] = "yo",  ['ж'] = "zh",  ['з'] = "z",   ['и'] = "i",
            ['й'] = "y",   ['к'] = "k",   ['л'] = "l",   ['м'] = "m",   ['н'] = "n",
            ['о'] = "o",   ['п'] = "p",   ['р'] = "r",   ['с'] = "s",   ['т'] = "t",
            ['у'] = "u",   ['ф'] = "f",   ['х'] = "kh",  ['ц'] = "ts",  ['ч'] = "ch",
            ['ш'] = "sh",  ['щ'] = "sch", ['ъ'] = "",    ['ы'] = "y",   ['ь'] = "",
            ['э'] = "e",   ['ю'] = "yu",  ['я'] = "ya",
            ['і'] = "i",   ['ї'] = "yi",  ['є'] = "ye",  ['ґ'] = "g"
        };

        private readonly MyDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;

        public BlogController(MyDbContext dbContext, UserManager<ApplicationUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }

        [HttpGet("topics")]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<BlogTopicDto>>> GetTopics([FromQuery] int skip = 0, [FromQuery] int take = 10)
        {
            if (take <= 0)
            {
                take = 10;
            }

            if (skip < 0)
            {
                skip = 0;
            }

            var topics = await _dbContext.BlogTopics
                .AsNoTracking()
                .Include(t => t.CreatedBy)
                .Include(t => t.Comments)
                    .ThenInclude(c => c.CreatedBy)
                .OrderByDescending(t => t.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            var result = topics.Select(MapTopic).ToList();
            return Ok(result);
        }

        [HttpPost("topics")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Moderator")]
        public async Task<ActionResult<BlogTopicDto>> CreateTopic([FromBody] CreateTopicRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            var title = request.Title.Trim();
            var text = request.Text.Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Title and text are required.");
            }

            var slug = await GenerateSlugAsync(title);
            var topic = new BlogTopic
            {
                Title = title,
                Content = text,
                CreatedAt = DateTime.UtcNow,
                CreatedById = user.Id,
                Slug = slug
            };

            _dbContext.BlogTopics.Add(topic);
            await _dbContext.SaveChangesAsync();

            await _dbContext.Entry(topic).Reference(t => t.CreatedBy).LoadAsync();

            var dto = MapTopic(topic);
            return Ok(dto);
        }

        [HttpPut("topics/{topicId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Moderator")]
        public async Task<ActionResult<BlogTopicDto>> UpdateTopic(int topicId, [FromBody] UpdateTopicRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var topic = await _dbContext.BlogTopics
                .Include(t => t.CreatedBy)
                .Include(t => t.Comments)
                    .ThenInclude(c => c.CreatedBy)
                .FirstOrDefaultAsync(t => t.Id == topicId);

            if (topic == null)
            {
                return NotFound();
            }

            var title = request.Title?.Trim() ?? string.Empty;
            var text = request.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Title and text are required.");
            }

            if (!string.Equals(topic.Title, title, StringComparison.Ordinal))
            {
                topic.Slug = await GenerateSlugAsync(title, topic.Id);
            }

            topic.Title = title;
            topic.Content = text;

            await _dbContext.SaveChangesAsync();

            return Ok(MapTopic(topic));
        }

        [HttpDelete("topics/{topicId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Moderator")]
        public async Task<IActionResult> DeleteTopic(int topicId)
        {
            var topic = await _dbContext.BlogTopics.FirstOrDefaultAsync(t => t.Id == topicId);
            if (topic == null)
            {
                return NotFound();
            }

            _dbContext.BlogTopics.Remove(topic);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("topics/{topicId:int}/comments")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<BlogCommentDto>> AddComment(int topicId, [FromBody] CreateCommentRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var topic = await _dbContext.BlogTopics.FirstOrDefaultAsync(t => t.Id == topicId);
            if (topic == null)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            var text = request.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Comment text is required.");
            }

            var comment = new BlogComment
            {
                TopicId = topic.Id,
                Content = text,
                CreatedAt = DateTime.UtcNow,
                CreatedById = user.Id
            };

            _dbContext.BlogComments.Add(comment);
            await _dbContext.SaveChangesAsync();

            await _dbContext.Entry(comment).Reference(c => c.CreatedBy).LoadAsync();

            var dto = MapComment(comment);
            return Ok(dto);
        }

        [HttpPut("topics/{topicId:int}/comments/{commentId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Moderator")]
        public async Task<ActionResult<BlogCommentDto>> UpdateComment(int topicId, int commentId, [FromBody] UpdateCommentRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var comment = await _dbContext.BlogComments
                .Include(c => c.CreatedBy)
                .FirstOrDefaultAsync(c => c.Id == commentId && c.TopicId == topicId);

            if (comment == null)
            {
                return NotFound();
            }

            var text = request.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest("Comment text is required.");
            }

            comment.Content = text;
            await _dbContext.SaveChangesAsync();

            return Ok(MapComment(comment));
        }

        [HttpDelete("topics/{topicId:int}/comments/{commentId:int}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Moderator")]
        public async Task<IActionResult> DeleteComment(int topicId, int commentId)
        {
            var comment = await _dbContext.BlogComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TopicId == topicId);
            if (comment == null)
            {
                return NotFound();
            }

            _dbContext.BlogComments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("topics/by-slug/{slug}")]
        [AllowAnonymous]
        public async Task<ActionResult<BlogTopicDto>> GetBySlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return BadRequest();
            }

            var topic = await _dbContext.BlogTopics
                .AsNoTracking()
                .Include(t => t.CreatedBy)
                .Include(t => t.Comments)
                    .ThenInclude(c => c.CreatedBy)
                .FirstOrDefaultAsync(t => t.Slug == slug);

            if (topic == null)
            {
                return NotFound();
            }

            return Ok(MapTopic(topic));
        }

        private async Task<string> GenerateSlugAsync(string title, int? excludeTopicId = null)
        {
            var transliterated = TransliterateToLatin(title);
            var normalized = Regex.Replace(transliterated.ToLowerInvariant(), "[^a-z0-9\\s-]", " ").Trim();
            normalized = Regex.Replace(normalized, "\\s+", "-");
            normalized = Regex.Replace(normalized, "-+", "-");
            normalized = normalized.Trim('-');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = Guid.NewGuid().ToString("n");
            }

            var slug = normalized;
            var index = 1;
            while (await _dbContext.BlogTopics.AnyAsync(t => t.Slug == slug && (!excludeTopicId.HasValue || t.Id != excludeTopicId.Value)))
            {
                slug = $"{normalized}-{index}";
                index++;
            }

            return slug;
        }

        private static string TransliterateToLatin(string source)
        {
            var lower = source.ToLowerInvariant();
            var builder = new StringBuilder(lower.Length * 2);

            foreach (var ch in lower)
            {
                if (TransliterationMap.TryGetValue(ch, out var mapped))
                {
                    builder.Append(mapped);
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static BlogTopicDto MapTopic(BlogTopic topic)
        {
            var comments = topic.Comments
                .OrderBy(c => c.CreatedAt)
                .Select(MapComment)
                .ToList();

            return new BlogTopicDto
            {
                Id = topic.Id,
                Slug = topic.Slug,
                Header = topic.Title,
                Text = topic.Content,
                UserId = topic.CreatedById,
                User = topic.CreatedBy?.DisplayName
                    ?? topic.CreatedBy?.UserName
                    ?? topic.CreatedBy?.Email
                    ?? string.Empty,
                CreatedAt = topic.CreatedAt,
                CommentCount = comments.Count,
                Comments = comments
            };
        }

        private static BlogCommentDto MapComment(BlogComment comment)
        {
            return new BlogCommentDto
            {
                Id = comment.Id,
                Text = comment.Content,
                UserId = comment.CreatedById,
                User = comment.CreatedBy?.DisplayName
                    ?? comment.CreatedBy?.UserName
                    ?? comment.CreatedBy?.Email
                    ?? string.Empty,
                CreatedAt = comment.CreatedAt
            };
        }

        public class CreateTopicRequest
        {
            [Required]
            [StringLength(256)]
            public string Title { get; set; } = string.Empty;

            [Required]
           // [StringLength(10000)]
            public string Text { get; set; } = string.Empty;
        }

        public class UpdateTopicRequest
        {
            [Required]
            [StringLength(256)]
            public string Title { get; set; } = string.Empty;

            [Required]
           // [StringLength(10000)]
            public string Text { get; set; } = string.Empty;
        }

        public class CreateCommentRequest
        {
            [Required]
            [StringLength(2000)]
            public string Text { get; set; } = string.Empty;
        }

        public class UpdateCommentRequest
        {
            [Required]
            [StringLength(2000)]
            public string Text { get; set; } = string.Empty;
        }
    }
}
