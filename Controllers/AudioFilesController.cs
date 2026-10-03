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
using YandexSpeech.Extensions;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AudioFilesController : ControllerBase
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".m4a", ".ogg", ".opus", ".webm", ".mp4", ".flac", ".aac"
        };
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
            if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
                return BadRequest("Unsupported audio file type.");

            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            using var stream = file.OpenReadStream();
            var audio = await _fileService.SaveOriginalAsync(stream, file.FileName, userId);
            return CreatedAtAction(nameof(GetById), new { id = audio.Id }, ToResponse(audio));
        }

        [HttpGet]
        public async Task<ActionResult> List()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var files = await _db.AudioFiles
                .Where(f => f.CreatedBy == userId)
                .Select(f => new {
                    f.Id,
                    f.OriginalFileName,
                    f.ConvertedFileName,
                    f.UploadedAt
                })
                .ToListAsync();

            return Ok(files);
        }

        // GET: api/AudioFiles/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AudioFile>> GetById(string id)
        {
            var userId = User.GetUserId();
            var file = await _db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id && f.CreatedBy == userId);
            if (file == null)
                return NotFound();
            return Ok(ToResponse(file));
        }

        // DELETE: api/AudioFiles/{id}
        // Удаляет запись и файлы
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = User.GetUserId();
            var file = await _db.AudioFiles.FirstOrDefaultAsync(f => f.Id == id && f.CreatedBy == userId);
            if (file == null)
                return NotFound();

            if (await _db.AudioWorkflowTasks.AnyAsync(t =>
                    t.AudioFileId == id && !t.Done && t.Status != RecognizeStatus.Error))
                return Conflict("Audio file is being recognized.");

            _db.AudioFiles.Remove(file);
            await _db.SaveChangesAsync();

            // Deleting the database record first keeps active tasks from losing their input.
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

            return NoContent();
        }

        private static object ToResponse(AudioFile file) => new
        {
            file.Id,
            file.OriginalFileName,
            file.ConvertedFileName,
            file.UploadedAt
        };
    }
}
