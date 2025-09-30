using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace YandexSpeech.services
{
    public interface IYandexDiskDownloadService
    {
        bool IsYandexDiskUrl(Uri uri);

        Task<YandexDiskDownloadResult> DownloadAsync(Uri uri, CancellationToken cancellationToken = default);
    }

    public sealed class YandexDiskDownloadResult : IAsyncDisposable, IDisposable
    {
        public YandexDiskDownloadResult(bool success, string? fileName, HttpResponseMessage? response, string? errorMessage)
        {
            Success = success;
            FileName = fileName;
            Response = response;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        public string? FileName { get; }

        public HttpResponseMessage? Response { get; }

        public string? ErrorMessage { get; }

        public void Dispose()
        {
            Response?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    public class YandexDiskDownloadService : IYandexDiskDownloadService
    {
        private static readonly Uri ApiBaseUri = new("https://cloud-api.yandex.net/v1/disk/public/resources/download");
        private static readonly HashSet<int> RetryStatusCodes = new() { 429, 500, 502, 503, 504 };
        private readonly IHttpClientFactory _httpClientFactory;

        public YandexDiskDownloadService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public bool IsYandexDiskUrl(Uri uri)
        {
            if (uri == null)
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            return host.Contains("disk.yandex") || host.Contains("yadi.sk");
        }

        public async Task<YandexDiskDownloadResult> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (!IsYandexDiskUrl(uri))
            {
                return new YandexDiskDownloadResult(false, null, null, "The provided URL is not a Yandex Disk link.");
            }

            var normalized = Normalize(uri);
            var client = _httpClientFactory.CreateClient();

            try
            {
                var href = await GetDownloadHrefAsync(client, normalized.PublicKey, normalized.Path, cancellationToken);
                return await DownloadFileAsync(client, href, cancellationToken);
            }
            catch (YandexDiskDownloadException ex)
            {
                return new YandexDiskDownloadResult(false, null, null, ex.Message);
            }
        }

        private static (string PublicKey, string? Path) Normalize(Uri uri)
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("disk.yandex"))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2 && string.Equals(segments[0], "d", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUri = $"{uri.Scheme}://{uri.Host}/d/{segments[1]}";
                    if (segments.Length > 2)
                    {
                        var rest = string.Join('/', segments.Skip(2));
                        var decoded = Uri.UnescapeDataString(rest);
                        return (baseUri, "/" + decoded);
                    }

                    return (baseUri, null);
                }

                if (segments.Length >= 2 && string.Equals(segments[0], "i", StringComparison.OrdinalIgnoreCase))
                {
                    return (uri.ToString(), null);
                }
            }

            return (uri.ToString(), null);
        }

        private static string GuessFileName(HttpResponseMessage response, string fallback)
        {
            var disposition = response.Content.Headers.ContentDisposition;
            var fileName = disposition?.FileNameStar ?? disposition?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName.Trim('\"');
            }

            var uri = response.RequestMessage?.RequestUri;
            if (uri != null)
            {
                var lastSegment = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(lastSegment))
                {
                    return lastSegment;
                }
            }

            return fallback;
        }

        private async Task<YandexDiskDownloadResult> DownloadFileAsync(HttpClient client, string href, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, href);
            request.Headers.UserAgent.ParseAdd("yadisk-downloader/1.0");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new YandexDiskDownloadException("Прямая ссылка устарела. Попробуйте получить новую ссылку и повторить загрузку.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = $"Не удалось скачать файл. Код ответа: {(int)response.StatusCode}";
                response.Dispose();
                throw new YandexDiskDownloadException(message);
            }

            var fallback = Path.GetFileName(response.RequestMessage?.RequestUri?.AbsolutePath) ?? "download.bin";
            var fileName = GuessFileName(response, fallback);
            return new YandexDiskDownloadResult(true, fileName, response, null);
        }

        private async Task<string> GetDownloadHrefAsync(HttpClient client, string publicKey, string? path, CancellationToken cancellationToken)
        {
            var query = new Dictionary<string, string?>
            {
                ["public_key"] = publicKey
            };

            if (!string.IsNullOrWhiteSpace(path))
            {
                query["path"] = path!.StartsWith('/') ? path : "/" + path;
            }

            var requestUri = QueryHelpers.AddQueryString(ApiBaseUri.ToString(), query);

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd("yadisk-downloader/1.0");

                var response = await client.SendAsync(request, cancellationToken);
                if (!RetryStatusCodes.Contains((int)response.StatusCode))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var href = await ExtractHrefAsync(response, cancellationToken);
                        response.Dispose();
                        return href;
                    }

                    var error = await ExtractErrorMessageAsync(response, cancellationToken);
                    response.Dispose();
                    throw new YandexDiskDownloadException(error);
                }

                if (attempt == 5)
                {
                    var error = await ExtractErrorMessageAsync(response, cancellationToken);
                    response.Dispose();
                    throw new YandexDiskDownloadException(error);
                }

                response.Dispose();

                var delaySeconds = Math.Pow(1.5, attempt);
                var delay = TimeSpan.FromSeconds(delaySeconds);
                await Task.Delay(delay, cancellationToken);
            }

            throw new YandexDiskDownloadException("Не удалось получить ссылку для скачивания с Яндекс.Диска.");
        }

        private static async Task<string> ExtractHrefAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("href", out var hrefElement)
                && hrefElement.ValueKind == JsonValueKind.String)
            {
                return hrefElement.GetString()!;
            }

            throw new YandexDiskDownloadException("Ответ API Яндекс.Диска не содержит ссылки для скачивания.");
        }

        private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.TryGetProperty("message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String)
                {
                    return messageElement.GetString()!;
                }

                if (document.RootElement.TryGetProperty("description", out var descriptionElement)
                    && descriptionElement.ValueKind == JsonValueKind.String)
                {
                    return descriptionElement.GetString()!;
                }

                if (document.RootElement.TryGetProperty("error", out var errorElement)
                    && errorElement.ValueKind == JsonValueKind.String)
                {
                    return errorElement.GetString()!;
                }

                return document.RootElement.ToString();
            }
            catch
            {
                return $"API Яндекс.Диска вернуло ошибку {(int)response.StatusCode}.";
            }
        }

        private class YandexDiskDownloadException : Exception
        {
            public YandexDiskDownloadException(string message) : base(message)
            {
            }
        }
    }
}
