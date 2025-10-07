using System.Net.Http.Headers;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YandexSpeech.Services;

namespace YandexSpeech.services
{
    public class YSpeechService : IYSpeechService
    {
        private readonly string yandexPassportOauthToken;
        private readonly string apiKey;
        private readonly string serviceAccountId;
        private readonly string folderId;
        private string accessKey;
        private string secretKey;
        private readonly string awsAccessKey;
        private readonly string awsSecretKey;
        private readonly string s3ServiceUrl;
        private readonly string defaultBucketName;
        private readonly OpusConversionService opusService = new OpusConversionService();

        public YSpeechService(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var section = configuration.GetSection("YSpeech");
            if (!section.Exists())
            {
                throw new InvalidOperationException("Configuration section 'YSpeech' is missing.");
            }

            yandexPassportOauthToken = GetRequiredSetting(section, "YandexPassportOauthToken");
            apiKey = GetRequiredSetting(section, "ApiKey");
            serviceAccountId = GetRequiredSetting(section, "ServiceAccountId");
            folderId = GetRequiredSetting(section, "FolderId");
            accessKey = GetRequiredSetting(section, "AccessKey");
            secretKey = GetRequiredSetting(section, "SecretKey");
            awsAccessKey = GetRequiredSetting(section, "AwsAccessKey");
            awsSecretKey = GetRequiredSetting(section, "AwsSecretKey");
            s3ServiceUrl = GetRequiredSetting(section, "S3ServiceUrl");
            defaultBucketName = GetRequiredSetting(section, "DefaultBucketName");
        }

        private static string GetRequiredSetting(IConfiguration configuration, string key)
        {
            return configuration[key]
                ?? throw new InvalidOperationException($"Configuration value 'YSpeech:{key}' is missing.");
        }

        public async Task<string> GetToken()
        {
            string url = "https://iam.api.cloud.yandex.net/iam/v1/tokens";
            var data = new Dictionary<string, string>
            {
                { "yandexPassportOauthToken", yandexPassportOauthToken },
            };
            string json = JsonConvert.SerializeObject(data);
            using (HttpClient client = new HttpClient())
            {
                HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(url, content);
                string result = await response.Content.ReadAsStringAsync();
                string iamToken = JObject.Parse(result)["iamToken"]?.ToString();
                return iamToken;
            }
        }

        /// <summary>
        /// Удаляет все файлы (объекты) из указанного бакета.
        /// Если bucketName = null, будет использован defaultBucketName.
        /// </summary>
        /// <param name="bucketName"></param>
        /// <returns></returns>
        public async Task DeleteAllFilesAsync(string bucketName = null)
        {
            bucketName ??= defaultBucketName;

            var config = new AmazonS3Config { ServiceURL = s3ServiceUrl };
            using var client = new AmazonS3Client(accessKey, secretKey, config);

            // Настраиваем запрос на получение списка объектов в бакете
            var listRequest = new ListObjectsV2Request { BucketName = bucketName, MaxKeys = 1000 };

            ListObjectsV2Response listResponse;

            do
            {
                listResponse = await client.ListObjectsV2Async(listRequest);
                if (listResponse.S3Objects.Count > 0)
                {
                    var deleteRequest = new DeleteObjectsRequest { BucketName = bucketName };

                    // Добавляем ключи всех объектов, которые хотим удалить
                    foreach (var s3Object in listResponse.S3Objects)
                    {
                        deleteRequest.AddKey(s3Object.Key);
                    }

                    // Удаляем объекты пачкой
                    await client.DeleteObjectsAsync(deleteRequest);
                }

                // Если в бакете много объектов, переходим к следующей "странице" с данными
                listRequest.ContinuationToken = listResponse.NextContinuationToken;
            } while (listResponse.IsTruncated ?? false);
        }

        public async Task<Tokens> GetKeys(string IAMTOKEN)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + IAMTOKEN);
                string url = "https://iam.api.cloud.yandex.net/iam/aws-compatibility/v1/accessKeys";
                var data = new Dictionary<string, string>
                {
                    { "serviceAccountId", serviceAccountId },
                };
                string json = JsonConvert.SerializeObject(data);
                HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(url, content);
                string result = await response.Content.ReadAsStringAsync();
                var a = JsonConvert.DeserializeObject<Tokens>(result);
                accessKey = a.AccessKey.KeyId;
                secretKey = a.Secret;
                return a;
            }
        }

        public async Task<GPTResult> GptAsk(string req)
        {
            string apiUrl = "https://llm.api.cloud.yandex.net/foundationModels/v1/completion";
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Api-Key",
                    apiKey
                );
                GptTask gpt = new GptTask("преобразуй слова в предложения", req);
                var content = JsonConvert.SerializeObject(gpt);
                var jsonContent = new StringContent(content, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiUrl, jsonContent);
                string result = await response.Content.ReadAsStringAsync();
                var r = result.Split('\n');
                var rr = r[r.Length - 2];
                return JsonConvert.DeserializeObject<GPTResult>(rr);
            }
        }

        public async Task<RecognizeResult> getRes(string operationId)
        {
            string url = $"https://operation.api.cloud.yandex.net/operations/{operationId}";
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Api-Key {apiKey}");
                HttpResponseMessage response = await client.GetAsync(url);
                string result = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<RecognizeResult>(result);
            }
        }

        public async Task<RecognizeResult> Recognize(string fileUri)
        {
            string url =
                "https://transcribe.api.cloud.yandex.net/speech/stt/v2/longRunningRecognize";
            var body = new
            {
                config = new { specification = new { languageCode = "ru-RU" } },
                audio = new { uri = fileUri },
            };
            string jsonContent = JsonConvert.SerializeObject(body);
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Api-Key {apiKey}");
                HttpContent content = new StringContent(
                    jsonContent,
                    Encoding.UTF8,
                    "application/json"
                );
                HttpResponseMessage response = await client.PostAsync(url, content);
                string result = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<RecognizeResult>(result);
            }
        }

        public async Task Speech(string iamToken, string text, string filename)
        {
            var values = new Dictionary<string, string>
            {
                { "text", text },
                { "lang", "ru-RU" },
                { "voice", "filipp" },
                { "folderId", folderId },
            };
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + iamToken);
                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync(
                    "https://tts.api.cloud.yandex.net/speech/v1/tts:synthesize",
                    content
                );
                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                File.WriteAllBytes(filename, responseBytes);
            }
        }

        public async Task<List<S3Bucket>> ListBucketsAsync(string accessKey, string secretKey)
        {
            var config = new AmazonS3Config { ServiceURL = s3ServiceUrl };
            using var client = new AmazonS3Client(accessKey, secretKey, config);
            var response = await client.ListBucketsAsync();
            return response.Buckets;
        }

        public async Task UploadFileToBucketAsync(
            string localFilePath,
            string bucketName,
            string objectKey
        )
        {
            var config = new AmazonS3Config { ServiceURL = s3ServiceUrl };
            using var s3Client = new AmazonS3Client(accessKey, secretKey, config);
            using var fileStream = File.OpenRead(localFilePath);
            var fileTransferUtility = new TransferUtility(s3Client);
            var request = new TransferUtilityUploadRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                InputStream = fileStream,
                StorageClass = S3StorageClass.Standard,
                CannedACL = S3CannedACL.Private,
                AutoCloseStream = false,
            };
            await fileTransferUtility.UploadAsync(request);
        }

        public async Task<RecognizeResult> UploadOpusAndRecognizeAsync(
            string localOpusFilePath,
            string bucketName = null,
            string objectKey = null
        )
        {
            bucketName ??= defaultBucketName;
            objectKey ??= Path.GetFileName(localOpusFilePath);
            await UploadFileToBucketAsync(localOpusFilePath, bucketName, objectKey);
            var fileUri =
                $"https://{s3ServiceUrl.Replace("https://", "")}/{bucketName}/{objectKey}";
            var recResult = await Recognize(fileUri);
            return recResult;
        }

        public async Task ConvertMp3ToOpusAsync(string inputMp3, string outputOpus)
        {
            opusService.ConvertToOpus(inputMp3, outputOpus);
            await Task.CompletedTask;
        }

        public async Task<string> ConvertMp3UploadAndRecognizeAsync(string inputMp3)
        {
            if (Path.GetExtension(inputMp3).ToLower() == ".opus")
            {
                return (await UploadOpusAndRecognizeAsync(inputMp3)).id;
            }
            string tempOpus = Path.ChangeExtension(inputMp3, ".opus");
            await ConvertMp3ToOpusAsync(inputMp3, tempOpus);
            string objectKey = Path.GetFileName(tempOpus);
            var operationId = (
                await UploadOpusAndRecognizeAsync(tempOpus, defaultBucketName, objectKey)
            ).id;
            return operationId;
        }

        public async Task<string> ConvertMp3UploadAndRecognizeAsyncOpus(string tempOpus)
        {
            string objectKey = Path.GetFileName(tempOpus);
            var operationId = (
                await UploadOpusAndRecognizeAsync(tempOpus, defaultBucketName, objectKey)
            ).id;
            return operationId;
        }
    }
}
