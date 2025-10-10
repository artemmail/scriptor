using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using YandexSpeech.models.DB;

namespace YandexSpeech.services
{
    public interface IPunctuationService
    {
        Task<string> GetAvailableModelsAsync();
        Task<string> FixPunctuationAsync(
            string rawText,
            string? previousContext,
            string profileName,
            string? clarification = null);
    }

    public class PunctuationService : IPunctuationService
    {
        private readonly MyDbContext _dbContext;
        private readonly string _openAiApiKey;
        private const int MaxRetries = 20;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
        private readonly Dictionary<string, RecognitionProfile> _profileCache =
            new(StringComparer.OrdinalIgnoreCase);

        public PunctuationService(MyDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
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
                    // Transient errors are retried below.
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay * attempt);
                }
            }

            throw new Exception($"Не удалось получить список моделей после {MaxRetries} попыток.");
        }

        public async Task<string> FixPunctuationAsync(
            string rawText,
            string? previousContext,
            string profileName,
            string? clarification = null)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                throw new ArgumentException("Raw text must be provided.", nameof(rawText));
            }

            var profile = await GetProfileAsync(profileName);

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = profile.Request
                }
            };

            if (!string.IsNullOrWhiteSpace(previousContext))
            {
                messages.Add(new { role = "assistant", content = previousContext });
            }

            if (!string.IsNullOrWhiteSpace(clarification))
            {
                var clarificationMessage = BuildClarificationMessage(profile, clarification);
                if (!string.IsNullOrWhiteSpace(clarificationMessage))
                {
                    messages.Add(new { role = "system", content = clarificationMessage });
                }
            }

            messages.Add(new { role = "user", content = rawText });

            var payload = JsonSerializer.Serialize(new
            {
                model = profile.OpenAiModel,
                messages,
                temperature = 0.0
            });

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");

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
                    // Retry below.
                }

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay * attempt);
                }
            }

            throw new Exception($"Не удалось получить ответ от сервера после {MaxRetries} попыток.");
        }

        private async Task<RecognitionProfile> GetProfileAsync(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                throw new ArgumentException("Profile name must be provided.", nameof(profileName));
            }

            if (_profileCache.TryGetValue(profileName, out var cached))
            {
                return cached;
            }

            var profile = await _dbContext.RecognitionProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name == profileName);

            if (profile == null)
            {
                throw new InvalidOperationException($"Recognition profile '{profileName}' was not found.");
            }

            _profileCache[profileName] = profile;
            return profile;
        }

        private static string BuildClarificationMessage(RecognitionProfile profile, string clarification)
        {
            var trimmed = clarification.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var template = profile.ClarificationTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                return $"Apply the following additional requirements while formatting: {trimmed}";
            }

            if (template.Contains("{clarification}"))
            {
                return template.Replace("{clarification}", trimmed);
            }

            if (template.Contains("{0}"))
            {
                return string.Format(template, trimmed);
            }

            return $"{template.Trim()} {trimmed}".Trim();
        }
    }
}
