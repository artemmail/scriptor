using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace YandexSpeech.services
{
    /// <summary>
    /// Alternative implementation of <see cref="IPunctuationService"/> that performs the final
    /// Markdown dialogue formatting while restoring punctuation for each segment. The resulting
    /// segments already contain speaker markup, allowing the formatting step of the transcription
    /// pipeline to simply concatenate the pieces without another OpenAI request.
    /// </summary>
    public class IntegratedFormattingPunctuationService : IPunctuationService
    {
        private readonly string _openAiApiKey;
        private const int MaxRetries = 20;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

        public IntegratedFormattingPunctuationService(IConfiguration configuration)
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
                    // The retry loop below will handle transient failures.
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay * attempt);
                }
            }

            throw new Exception($"Не удалось получить список моделей после {MaxRetries} попыток.");
        }

        public async Task<string> FixPunctuationAsync(string rawText, string previousContext, string? clarification = null)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = @"You transform raw speech recognition output into the final Markdown dialogue.
Restore punctuation, split long monologues into natural sentences and attribute every replica to a speaker.
Use bold speaker labels followed by a colon (e.g. **Speaker 1:**) and preserve the narrative style from the
previously formatted segment that can be provided as assistant context. Do not add explanations or commentary."
                }
            };

            if (!string.IsNullOrWhiteSpace(previousContext))
            {
                messages.Add(new { role = "assistant", content = previousContext });
            }

            if (!string.IsNullOrWhiteSpace(clarification))
            {
                messages.Add(new
                {
                    role = "system",
                    content = $"Apply the following additional requirements while formatting: {clarification.Trim()}"
                });
            }

            messages.Add(new { role = "user", content = rawText });

            var requestBody = new
            {
                model = "gpt-4.1-mini",
                messages,
                temperature = 0.0,
            };

            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

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
                catch (HttpRequestException)
                {
                    // Transient errors are retried by the loop.
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay);
                }
            }

            throw new Exception($"Не удалось получить ответ от сервера после {MaxRetries} попыток.");
        }
    }
}
