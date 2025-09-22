using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public interface IAudioFileService
    {
        Task<AudioFile> SaveOriginalAsync(Stream stream, string originalFileName, string createdBy);
        Task<AudioFile> ConvertToOpusAsync(string audioFileId);
        Task<IEnumerable<AudioFile>> GetAllAsync();
        Task<AudioFile?> GetByIdAsync(string audioFileId);
    }
}
