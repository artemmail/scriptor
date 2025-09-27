using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace YandexSpeech.services.Whisper
{
    public sealed class FasterWhisperTranscriptionService : IWhisperTranscriptionService
    {
        private readonly ILogger<FasterWhisperTranscriptionService> _logger;
        private readonly FasterWhisperQueueClient _queueClient;
        private readonly string _model;
        private readonly string _device;
        private readonly string _computeType;
        private readonly string _language;
        private readonly IReadOnlyList<double> _temperatures;
        private readonly double _compressionRatioThreshold;
        private readonly double _logProbThreshold;
        private readonly double _noSpeechThreshold;
        private readonly bool _conditionOnPreviousText;
        private readonly string _temperatureLiteral;
        private readonly string _compressionLiteral;
        private readonly string _logProbLiteral;
        private readonly string _noSpeechLiteral;
        private readonly string _conditionLiteral;

        public FasterWhisperTranscriptionService(
            IConfiguration configuration,
            ILogger<FasterWhisperTranscriptionService> logger,
            FasterWhisperQueueClient queueClient)
        {
            _logger = logger;
            _queueClient = queueClient;

            var section = configuration.GetSection("FasterWhisper");
            _model = section.GetValue<string>("Model") ?? configuration.GetValue<string>("Whisper:Model") ?? "medium";
            _device = section.GetValue<string>("Device") ?? configuration.GetValue<string>("Whisper:Device") ?? "cpu";
            _computeType = section.GetValue<string>("ComputeType") ?? "int8";
            _language = section.GetValue<string>("Language")
                ?? configuration.GetValue<string>("Whisper:Language")
                ?? "ru";

            _temperatures = ParseTemperatures(section) ?? new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 };
            _compressionRatioThreshold = section.GetValue<double?>("CompressionRatioThreshold") ?? 2.4;
            _logProbThreshold = section.GetValue<double?>("LogProbThreshold") ?? -1.0;
            _noSpeechThreshold = section.GetValue<double?>("NoSpeechThreshold") ?? 0.6;
            _conditionOnPreviousText = section.GetValue<bool?>("ConditionOnPreviousText") ?? true;

            _temperatureLiteral = BuildTemperatureLiteral(_temperatures);
            _compressionLiteral = FormatDouble(_compressionRatioThreshold);
            _logProbLiteral = FormatDouble(_logProbThreshold);
            _noSpeechLiteral = FormatDouble(_noSpeechThreshold);
            _conditionLiteral = _conditionOnPreviousText ? "True" : "False";
        }

        private static IReadOnlyList<double>? ParseTemperatures(IConfiguration section)
        {
            var array = section.GetSection("Temperatures").Get<double[]?>();
            if (array is { Length: > 0 })
                return Array.AsReadOnly(array);

            var temperaturesValue = section.GetValue<string?>("Temperatures");
            if (string.IsNullOrWhiteSpace(temperaturesValue))
                return null;

            var parts = temperaturesValue
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return null;

            var values = new List<double>();
            foreach (var part in parts)
            {
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    values.Add(parsed);
            }

            return values.Count == 0 ? null : values.AsReadOnly();
        }

        public async Task<WhisperTranscriptionResult> TranscribeAsync(
            string audioFilePath,
            string workingDirectory,
            string? ffmpegExecutable,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(audioFilePath))
                throw new ArgumentException("Audio file path must be provided.", nameof(audioFilePath));

            var audioPath = Path.GetFullPath(audioFilePath);
            if (!File.Exists(audioPath))
                throw new FileNotFoundException("Audio file not found for Whisper transcription.", audioPath);

            _ = workingDirectory; // working directory is managed by the microservice

            var request = new FasterWhisperQueueRequest
            {
                Audio = audioPath,
                Model = _model,
                Device = _device,
                ComputeType = NormalizeCt2(_device, _computeType),
                Language = _language,
                Temperature = _temperatureLiteral,
                CompressionRatioThreshold = _compressionLiteral,
                LogProbThreshold = _logProbLiteral,
                NoSpeechThreshold = _noSpeechLiteral,
                ConditionOnPreviousText = _conditionLiteral,
                FfmpegExecutable = ffmpegExecutable
            };

            FasterWhisperQueueResponse response;
            try
            {
                _logger.LogInformation("Dispatching FasterWhisper transcription via RabbitMQ: {File}", audioPath);
                response = await _queueClient.TranscribeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Transcription cancelled for {File}", audioPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch FasterWhisper transcription for {File}", audioPath);
                throw;
            }

            if (!response.Success)
            {
                var errorMessage = string.IsNullOrWhiteSpace(response.Error) ? "Unknown error" : response.Error;
                if (!string.IsNullOrWhiteSpace(response.Diagnostics))
                {
                    _logger.LogError("FasterWhisper transcription failed for {File}: {Error}\n{Diagnostics}",
                        audioPath, errorMessage, response.Diagnostics);
                }
                else
                {
                    _logger.LogError("FasterWhisper transcription failed for {File}: {Error}", audioPath, errorMessage);
                }

                throw new InvalidOperationException($"FasterWhisper transcription failed: {errorMessage}");
            }

            var transcription = response.TranscriptJson;
            if (string.IsNullOrWhiteSpace(transcription))
                throw new InvalidOperationException("FasterWhisper transcription result is empty.");

            WhisperTranscriptionResponse? parsed;
            try
            {
                parsed = WhisperTranscriptionHelper.Parse(transcription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse FasterWhisper transcription payload for {File}", audioPath);
                throw;
            }

            if (parsed?.Segments == null || parsed.Segments.Count == 0)
                throw new InvalidOperationException("FasterWhisper transcription does not contain segments.");

            var timecodedText = WhisperTranscriptionHelper.BuildTimecodedText(parsed);

            return new WhisperTranscriptionResult
            {
                TimecodedText = timecodedText,
                RawJson = transcription
            };
        }

        private static string NormalizeCt2(string device, string computeType)
        {
            var d = (device ?? "cpu").Trim().ToLowerInvariant();
            var ct = (computeType ?? string.Empty).Trim().ToLowerInvariant();

            if (d == "cuda" || d == "gpu" || d.StartsWith("cuda:"))
            {
                return ct switch
                {
                    "float16" => "float16",
                    "int8_float16" => "int8_float16",
                    "float32" => "float32",
                    _ => "float16"
                };
            }

            return ct switch
            {
                "int8" => "int8",
                "float32" => "float32",
                "float16" => "float16",
                _ => "int8"
            };
        }

        private static string BuildTemperatureLiteral(IReadOnlyCollection<double> temperatures)
        {
            if (temperatures.Count == 0)
                return "0.0";

            if (temperatures.Count == 1)
                return FormatDouble(temperatures.First());

            return "(" + string.Join(", ", temperatures.Select(FormatDouble)) + ")";
        }

        private static string FormatDouble(double value)
        {
            var formatted = value.ToString("0.###############################", CultureInfo.InvariantCulture);
            if (!formatted.Contains('.') && !formatted.Contains('e') && !formatted.Contains('E'))
                formatted += ".0";
            return formatted;
        }
    }
}
