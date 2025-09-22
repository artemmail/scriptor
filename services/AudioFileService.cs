// AudioFileService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public class AudioFileService : IAudioFileService
    {
        private readonly MyDbContext _db;
        private readonly IYSpeechService _ySpeechService;
        private readonly string _storageRoot;

        public AudioFileService(
     MyDbContext dbContext,
     IYSpeechService ySpeechService,
     IConfiguration configuration
 )
        {
            _db = dbContext;
            _ySpeechService = ySpeechService;
            _storageRoot = "c:/Temp";// /* configuration.GetValue<string>("Storage:RootPath")                ?? throw new InvalidOperationException("Storage:RootPath not configured");*/
        }

        // Сохраняет оригинальный файл с привязкой к пользователю
        public async Task<AudioFile> SaveOriginalAsync(Stream stream, string originalFileName, string createdBy)
        {
            var id = Guid.NewGuid().ToString();
            var ext = Path.GetExtension(originalFileName);
            var fileName = $"{id}{ext}";
            var folder = Path.Combine(_storageRoot, "original");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                await stream.CopyToAsync(fs);

            var entity = new AudioFile
            {
                Id = id,
                OriginalFileName = originalFileName,
                OriginalFilePath = path,
                UploadedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
            _db.AudioFiles.Add(entity);
            await _db.SaveChangesAsync();

            return entity;
        }

        public async Task<AudioFile> ConvertToOpusAsync(string audioFileId)
        {
            var file = await _db.AudioFiles.FindAsync(audioFileId)
                       ?? throw new InvalidOperationException($"AudioFile {audioFileId} not found");

            if (!string.IsNullOrEmpty(file.ConvertedFilePath) && File.Exists(file.ConvertedFilePath))
                return file;

            var folder = Path.Combine(_storageRoot, "converted");
            Directory.CreateDirectory(folder);
            var outName = $"{file.Id}.opus";
            var outPath = Path.Combine(folder, outName);

            await _ySpeechService.ConvertMp3ToOpusAsync(file.OriginalFilePath, outPath);

            file.ConvertedFileName = outName;
            file.ConvertedFilePath = outPath;
            file.ConvertedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return file;
        }

        public async Task<IEnumerable<AudioFile>> GetAllAsync()
        {
            return await _db.AudioFiles
                .OrderByDescending(a => a.UploadedAt)
                .ToListAsync();
        }

        public async Task<AudioFile?> GetByIdAsync(string audioFileId)
        {
            return await _db.AudioFiles
                .FirstOrDefaultAsync(a => a.Id == audioFileId);
        }
    }
}
