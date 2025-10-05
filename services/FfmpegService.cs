using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public class FfmpegService : IFfmpegService
    {
        private readonly string? _configuredPath;

        public FfmpegService(IConfiguration configuration)
        {
            _configuredPath = configuration.GetValue<string>("FfmpegExePath");
        }

        public async Task ConvertToWav16kMonoAsync(
            string sourcePath,
            string outputPath,
            CancellationToken cancellationToken = default,
            string? overrideExecutable = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path must be provided.", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path must be provided.", nameof(outputPath));

            var ffmpegExecutable = ResolveFfmpegExecutable(overrideExecutable);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegExecutable,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-vn");
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add("16000");
            startInfo.ArgumentList.Add("-acodec");
            startInfo.ArgumentList.Add("pcm_s16le");
            startInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to start ffmpeg: {ex.Message}", ex);
            }

            var waitTask = process.WaitForExitAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            await Task.WhenAll(waitTask, stderrTask, stdoutTask).ConfigureAwait(false);

            var standardError = await stderrTask.ConfigureAwait(false);
            var standardOutput = await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg conversion failed (exit code {process.ExitCode}): {standardError}\n{standardOutput}");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"FFmpeg conversion did not produce an output file at '{outputPath}'. Output: {standardOutput} Error: {standardError}");
            }
        }

        public string ResolveFfmpegExecutable(string? overrideExecutable = null)
        {
            var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

            if (!string.IsNullOrWhiteSpace(overrideExecutable))
            {
                var resolvedOverride = ResolveCandidate(overrideExecutable, executableName);
                if (resolvedOverride != null)
                {
                    return resolvedOverride;
                }

                return overrideExecutable;
            }

            if (!string.IsNullOrWhiteSpace(_configuredPath))
            {
                var resolvedConfigured = ResolveCandidate(_configuredPath, executableName);
                if (resolvedConfigured != null)
                {
                    return resolvedConfigured;
                }
            }

            return executableName;
        }

        public string? ResolveFfmpegDirectory(string? overrideExecutable = null)
        {
            if (!string.IsNullOrWhiteSpace(overrideExecutable))
            {
                if (Directory.Exists(overrideExecutable))
                {
                    return overrideExecutable;
                }

                var candidate = ResolveCandidate(overrideExecutable, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (!string.IsNullOrWhiteSpace(candidate) && Path.IsPathRooted(candidate))
                {
                    return Path.GetDirectoryName(candidate);
                }
            }

            if (!string.IsNullOrWhiteSpace(_configuredPath))
            {
                if (Directory.Exists(_configuredPath))
                {
                    return _configuredPath;
                }

                var resolved = ResolveCandidate(_configuredPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (!string.IsNullOrWhiteSpace(resolved) && Path.IsPathRooted(resolved))
                {
                    return Path.GetDirectoryName(resolved);
                }
            }

            var executable = ResolveFfmpegExecutable();
            if (Path.IsPathRooted(executable))
            {
                return Path.GetDirectoryName(executable);
            }

            return null;
        }

        private static string? ResolveCandidate(string value, string executableName)
        {
            if (File.Exists(value))
            {
                return value;
            }

            if (Directory.Exists(value))
            {
                var candidate = Path.Combine(value, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
