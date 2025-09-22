using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YandexSpeech.services;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AudioFilesController : ControllerBase
    {
        private readonly IAudioFileService _fileService;
        private readonly MyDbContext _db;

        public AudioFilesController(
            IAudioFileService fileService,
            MyDbContext db
        )
        {
            _fileService = fileService;
            _db = db;
        }

        // POST: api/AudioFiles
        // Загружает файл и сохраняет пользователя
        [HttpPost]
        [RequestSizeLimit(200_000_000)]      // 200 Мб
        [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
        public async Task<ActionResult<AudioFile>> Upload([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File not provided.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            using var stream = file.OpenReadStream();
            var audio = await _fileService.SaveOriginalAsync(stream, file.FileName, userId);
            return CreatedAtAction(nameof(GetById), new { id = audio.Id }, audio);
        }

        [HttpGet]
        public async Task<ActionResult> List()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var files = await _db.AudioFiles
                .Where(f => f.CreatedBy == userId)
                .Select(f => new {
                    f.Id,
                    OriginalFileName = f.OriginalFileName,  // ← обязательно выводим его
                    f.OriginalFilePath,
                    f.ConvertedFileName,
                    f.ConvertedFilePath,
                    f.UploadedAt
                })
                .ToListAsync();

            return Ok(files);
        }

        // GET: api/AudioFiles/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AudioFile>> GetById(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var file = await _db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id && f.CreatedBy == userId);
            if (file == null)
                return NotFound();
            return Ok(file);
        }

        // DELETE: api/AudioFiles/{id}
        // Удаляет запись и файлы
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var file = await _db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id && f.CreatedBy == userId);
            if (file == null)
                return NotFound();

            // Удаление физических файлов
            try
            {
                if (System.IO.File.Exists(file.OriginalFilePath))
                    System.IO.File.Delete(file.OriginalFilePath);
                if (!string.IsNullOrEmpty(file.ConvertedFilePath) && System.IO.File.Exists(file.ConvertedFilePath))
                    System.IO.File.Delete(file.ConvertedFilePath);
            }
            catch
            {
                // логирование при необходимости
            }

            _db.AudioFiles.Remove(file);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
