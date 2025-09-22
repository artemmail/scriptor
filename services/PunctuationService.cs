using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace YandexSpeech.services
{
    public interface IPunctuationService
    {
        Task<string> GetAvailableModelsAsync();
        Task<string> FixPunctuationAsync(string rawText, string previousContext);
    }

    public class PunctuationService : IPunctuationService
    {
        private readonly string _openAiApiKey;
        private const int MaxRetries = 20;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

        public PunctuationService(IConfiguration configuration)
        {
            _openAiApiKey = configuration["OpenAI:ApiKey"]
                ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        }

        public async Task<string> GetAvailableModelsAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var response = await client.GetAsync("https://api.openai.com/v1/models");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var models))
                        {
                            return JsonSerializer.Serialize(models, new JsonSerializerOptions { WriteIndented = true });
                        }
                        return "No models available.";
                    }
                }
                catch (HttpRequestException)
                {
                    // логирование, если нужно
                }

                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelay*attempt);
            }

            throw new Exception($"Не удалось получить список моделей после {MaxRetries} попыток.");
        }

        public async Task<string> FixPunctuationAsync(string rawText, string previousContext)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            // Собираем список сообщений
            var messages = new List<object>
            {
                new {
                    role = "system",
                    content = @"You are a service that adds punctuation and Markdown formatting to recognized audio text.
Ensure that if a previous formatted segment is provided, the style remains consistent in the new segment.
Do not include any additional comments or explanations."
                }
            };
            if (!string.IsNullOrWhiteSpace(previousContext))
            {
                messages.Add(new { role = "assistant", content = previousContext });
            }
            messages.Add(new { role = "user", content = rawText });

            var requestBody = new
            {
                model = "gpt-4.1-mini",
                messages,
                temperature = 0.0,
            };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var choices = doc.RootElement.GetProperty("choices");
                        if (choices.GetArrayLength() > 0)
                        {
                            return choices[0]
                                .GetProperty("message")
                                .GetProperty("content")
                                .GetString()!
                                .Trim();
                        }
                        return string.Empty;
                    }
                }
                catch (HttpRequestException ex)
                {
                    // логирование, если нужно
                }

                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelay);
            }

            throw new Exception($"Не удалось получить ответ от сервера после {MaxRetries} попыток.");
        }
    }
}
