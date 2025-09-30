using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    /// <summary>
    /// Alternative transcription service that relies on segments to produce the final Markdown output.
    /// Segment processing already performs the dialogue formatting, therefore the final formatting step
    /// only concatenates processed segments without issuing an additional OpenAI request.
    /// </summary>
    public class IntegratedFormattingOpenAiTranscriptionService : OpenAiTranscriptionService
    {
        public IntegratedFormattingOpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IPunctuationService punctuationService,
            IWhisperTranscriptionService whisperTranscriptionService,
            IHttpClientFactory httpClientFactory,
            IYandexDiskDownloadService yandexDiskDownloadService)
            : base(
                dbContext,
                configuration,
                loggerFactory.CreateLogger<OpenAiTranscriptionService>(),
                punctuationService,
                whisperTranscriptionService,
                httpClientFactory,
                yandexDiskDownloadService)
        {
        }

        protected override Task<string> CreateDialogueMarkdownAsync(string transcription, string? clarification)
        {
            // Segment processing already produced final Markdown, so we simply return the concatenated text.
            return Task.FromResult(transcription);
        }
    }
}
