using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace YandexSpeech.services.Whisper
{
    public sealed class WhisperCliTranscriptionService : IWhisperTranscriptionService
    {
        private readonly ILogger<WhisperCliTranscriptionService> _logger;
        private readonly string _whisperExecutableSetting;
        private readonly string _whisperModel;
        private readonly string _whisperDevice;

        public WhisperCliTranscriptionService(
            IConfiguration configuration,
            ILogger<WhisperCliTranscriptionService> logger)
        {
            _logger = logger;

            var whisperSection = configuration.GetSection("Whisper");
            _whisperExecutableSetting = whisperSection.GetValue<string>("ExecutablePath") ?? "whisper";
            _whisperModel = whisperSection.GetValue<string>("Model") ?? "medium";

            var configuredDevice = whisperSection.GetValue<string>("Device");
            if (!string.IsNullOrWhiteSpace(configuredDevice))
            {
                _whisperDevice = configuredDevice;
            }
            else
            {
                var useGpu = whisperSection.GetValue<bool?>("UseGpu") ?? false;
                _whisperDevice = useGpu ? "cuda" : "cpu";
            }
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

            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = Path.Combine(Path.GetTempPath(), "openai-transcriptions");
            }

            Directory.CreateDirectory(workingDirectory);

            var outputDirectory = Path.Combine(workingDirectory, $"whisper-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDirectory);

            var startInfo = BuildProcessStartInfo(audioPath, outputDirectory, ffmpegExecutable);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();

                var standardErrorTask = process.StandardError.ReadToEndAsync();
                var standardOutputTask = process.StandardOutput.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(standardErrorTask, standardOutputTask);

                var standardError = await standardErrorTask;
                var standardOutput = await standardOutputTask;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Whisper transcription failed (exit code {process.ExitCode}): {standardError}\n{standardOutput}");
                }

                var transcriptFile = WhisperTranscriptionHelper.FindFirstJsonFile(outputDirectory);
                if (transcriptFile == null)
                {
                    throw new InvalidOperationException($"Whisper transcription did not produce a JSON file. Output: {standardOutput} Error: {standardError}");
                }

                var transcription = await File.ReadAllTextAsync(transcriptFile, cancellationToken);
                if (string.IsNullOrWhiteSpace(transcription))
                    throw new InvalidOperationException("Whisper transcription result is empty.");

                var parsed = WhisperTranscriptionHelper.Parse(transcription);
                if (parsed?.Segments == null || parsed.Segments.Count == 0)
                    throw new InvalidOperationException("Whisper transcription does not contain segments.");

                var timecodedText = WhisperTranscriptionHelper.BuildTimecodedText(parsed);

                return new WhisperTranscriptionResult
                {
                    TimecodedText = timecodedText,
                    RawJson = transcription
                };
            }
            finally
            {
                CleanupDirectory(outputDirectory);
            }
        }

        private ProcessStartInfo BuildProcessStartInfo(string audioPath, string outputDirectory, string? ffmpegExecutable)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveWhisperExecutable(),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            EnsureEnvironmentEncoding(startInfo);
            ConfigureFfmpegEnvironment(startInfo, ffmpegExecutable);

            startInfo.ArgumentList.Add(audioPath);
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(_whisperModel);
            startInfo.ArgumentList.Add("--task");
            startInfo.ArgumentList.Add("transcribe");
            startInfo.ArgumentList.Add("--output_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--word_timestamps");
            startInfo.ArgumentList.Add("True");
            startInfo.ArgumentList.Add("--output_dir");
            startInfo.ArgumentList.Add(outputDirectory);

            if (!string.IsNullOrWhiteSpace(_whisperDevice))
            {
                startInfo.ArgumentList.Add("--device");
                startInfo.ArgumentList.Add(_whisperDevice);

                if (string.Equals(_whisperDevice, "cpu", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.ArgumentList.Add("--fp16");
                    startInfo.ArgumentList.Add("False");
                }
            }

            return startInfo;
        }

        private string ResolveWhisperExecutable()
        {
            if (File.Exists(_whisperExecutableSetting))
                return _whisperExecutableSetting;

            if (Directory.Exists(_whisperExecutableSetting))
            {
                var executableName = OperatingSystem.IsWindows() ? "whisper.exe" : "whisper";
                var candidate = Path.Combine(_whisperExecutableSetting, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return _whisperExecutableSetting;
        }

        private static void EnsureEnvironmentEncoding(ProcessStartInfo startInfo)
        {
            if (!startInfo.Environment.ContainsKey("PYTHONIOENCODING"))
            {
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            }

            if (OperatingSystem.IsWindows())
            {
                startInfo.Environment["PYTHONUTF8"] = "1";
            }

            startInfo.StandardErrorEncoding = Encoding.UTF8;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
        }

        private static void ConfigureFfmpegEnvironment(ProcessStartInfo startInfo, string? ffmpegExecutable)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExecutable))
            {
                return;
            }

            startInfo.Environment["FFMPEG_BINARY"] = ffmpegExecutable;

            var ffmpegDirectory = Path.GetDirectoryName(ffmpegExecutable);
            if (string.IsNullOrWhiteSpace(ffmpegDirectory) || !Directory.Exists(ffmpegDirectory))
            {
                return;
            }

            var pathVariableName = OperatingSystem.IsWindows() ? "Path" : "PATH";
            if (!startInfo.Environment.TryGetValue(pathVariableName, out var currentPath) || string.IsNullOrWhiteSpace(currentPath))
            {
                currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            }

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                startInfo.Environment[pathVariableName] = ffmpegDirectory;
                return;
            }

            var segments = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (!segments.Any(p => string.Equals(p, ffmpegDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            {
                startInfo.Environment[pathVariableName] = string.Concat(ffmpegDirectory, Path.PathSeparator, currentPath);
            }
        }

        private void CleanupDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove Whisper output directory {OutputDirectory}", directory);
            }
        }
    }
}
