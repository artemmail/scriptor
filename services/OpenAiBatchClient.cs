using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YandexSpeech.services;

// Batch input and output use the same Chat Completions body as a direct request.
public sealed class OpenAiBatchClient
{
    private const string BaseUrl = "https://api.openai.com/v1/";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public OpenAiBatchClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<string> CompleteAsync(object body, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI:ApiKey is not configured.");

        var client = _httpClientFactory.CreateClient(nameof(OpenAiBatchClient));
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        if (!_configuration.GetValue("OpenAI:UseBatch", true))
        {
            using var requestContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var directResponse = await client.PostAsync("chat/completions", requestContent, cancellationToken);
            using var directJson = await ReadSuccessJsonAsync(directResponse, cancellationToken);
            return ReadCompletionText(directJson.RootElement);
        }

        var customId = Guid.NewGuid().ToString("N");
        var line = JsonSerializer.Serialize(new
        {
            custom_id = customId,
            method = "POST",
            url = "/v1/chat/completions",
            body
        }) + "\n";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("batch"), "purpose");
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(line));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl");
        form.Add(file, "file", "batch.jsonl");
        using var uploaded = await client.PostAsync("files", form, cancellationToken);
        using var uploadJson = await ReadSuccessJsonAsync(uploaded, cancellationToken);
        var fileId = uploadJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Batch input file has no ID.");

        using var batchContent = new StringContent(JsonSerializer.Serialize(new
        {
            input_file_id = fileId,
            endpoint = "/v1/chat/completions",
            completion_window = "24h"
        }), Encoding.UTF8, "application/json");
        using var created = await client.PostAsync("batches", batchContent, cancellationToken);
        using var createdJson = await ReadSuccessJsonAsync(created, cancellationToken);
        var batchId = createdJson.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Batch has no ID.");

        var deadline = DateTime.UtcNow.AddHours(25);
        while (true)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Batch {batchId} did not finish within 25 hours.");
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            using var statusResponse = await client.GetAsync($"batches/{batchId}", cancellationToken);
            using var statusJson = await ReadSuccessJsonAsync(statusResponse, cancellationToken);
            var status = statusJson.RootElement.GetProperty("status").GetString();
            if (status == "completed")
            {
                var outputId = statusJson.RootElement.GetProperty("output_file_id").GetString();
                if (string.IsNullOrWhiteSpace(outputId))
                    throw new InvalidOperationException($"Batch {batchId} completed without an output file.");

                using var outputResponse = await client.GetAsync($"files/{outputId}/content", cancellationToken);
                var output = await outputResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!outputResponse.IsSuccessStatusCode)
                    throw new HttpRequestException($"Batch output HTTP {(int)outputResponse.StatusCode}: {output}");

                foreach (var resultLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var result = JsonDocument.Parse(resultLine);
                    var root = result.RootElement;
                    if (root.GetProperty("custom_id").GetString() != customId)
                        continue;
                    var response = root.GetProperty("response");
                    if (response.ValueKind == JsonValueKind.Null || response.GetProperty("status_code").GetInt32() != 200)
                        throw new InvalidOperationException($"Batch request failed: {resultLine}");
                    return ReadCompletionText(response.GetProperty("body"));
                }
                throw new InvalidOperationException($"Batch {batchId} has no result for {customId}.");
            }
            if (status is "failed" or "expired" or "cancelled")
                throw new InvalidOperationException($"Batch {batchId} ended with status {status}: {statusJson.RootElement}");
        }
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI HTTP {(int)response.StatusCode}: {json}");
        return JsonDocument.Parse(json);
    }

    private static string ReadCompletionText(JsonElement body)
    {
        var choice = body.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.GetString() != "stop")
            throw new InvalidOperationException($"Постобработка не завершена: {reason.GetString() ?? "unknown"}.");
        var text = choice.GetProperty("message").GetProperty("content").GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException("OpenAI response has no text.");
    }
}
