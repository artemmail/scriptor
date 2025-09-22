using Amazon.S3.Model;

namespace YandexSpeech.services
{
    public interface IYSpeechService
    {
        Task<string> GetToken();
        Task<Tokens> GetKeys(string IAMTOKEN);
        Task<GPTResult> GptAsk(string req);
        Task<RecognizeResult> getRes(string operationId);
        Task<RecognizeResult> Recognize(string fileUri);

        Task Speech(string iamToken, string text, string filename);
        Task<List<S3Bucket>> ListBucketsAsync(string accessKey, string secretKey);
        Task UploadFileToBucketAsync(string localFilePath, string bucketName, string objectKey);
        Task<RecognizeResult> UploadOpusAndRecognizeAsync(
            string localOpusFilePath,
            string bucketName = null,
            string objectKey = null
        );
        Task ConvertMp3ToOpusAsync(string inputMp3, string outputOpus);
        Task<string> ConvertMp3UploadAndRecognizeAsync(string inputMp3);
    }
}
