using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    /// <summary>
    /// Alternative transcription service that relies on segments to produce the final Markdown output.
    /// Segment processing already performs the dialogue formatting, therefore the final formatting step
    /// only concatenates processed segments without issuing an additional OpenAI request.
    /// </summary>
    public class IntegratedFormattingOpenAiTranscriptionService : OpenAiTranscriptionService
    {
        protected override string SegmentProcessingProfileName => RecognitionProfileNames.IntegratedFormatting;

        public IntegratedFormattingOpenAiTranscriptionService(
            MyDbContext dbContext,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IPunctuationService punctuationService,
            IWhisperTranscriptionService whisperTranscriptionService,
            IHttpClientFactory httpClientFactory,
            IYandexDiskDownloadService yandexDiskDownloadService,
            IFfmpegService ffmpegService,
            ISubscriptionService subscriptionService)
            : base(
                dbContext,
                configuration,
                loggerFactory.CreateLogger<OpenAiTranscriptionService>(),
                punctuationService,
                whisperTranscriptionService,
                httpClientFactory,
                yandexDiskDownloadService,
                ffmpegService,
                subscriptionService)
        {
        }

        
    }
}
