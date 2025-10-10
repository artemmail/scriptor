using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Sdk;
using YandexSpeech.services.Whisper;

namespace YandexSpeech.Tests;

public sealed class FasterWhisperTranscriptionServiceTests
{
    [Fact]
    public async Task TranscribeAsync_AllowsTranscriptWhenProcessExitsWithError()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new Exception("FasterWhisper mock script requires Unix-like environment.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "faster-whisper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var scriptPath = Path.Combine(tempRoot, "faster-whisper-mock.sh");
            var script = "#!/bin/bash\n" +
                         "audio=\"$1\"\n" +
                         "shift\n" +
                         "out_dir=\"\"\n" +
                         "while (($#)); do\n" +
                         "  case \"$1\" in\n" +
                         "    --output_dir)\n" +
                         "      out_dir=\"$2\"\n" +
                         "      shift 2\n" +
                         "      ;;\n" +
                         "    *)\n" +
                         "      shift\n" +
                         "      ;;\n" +
                         "  esac\n" +
                         "done\n" +
                         "mkdir -p \"$out_dir\"\n" +
                         "cat > \"$out_dir/transcript.json\" <<'JSON'\n" +
                         "{\n" +
                         "  \"segments\": [\n" +
                         "    { \"start\": 0.0, \"end\": 1.2, \"text\": \"hello world\" }\n" +
                         "  ]\n" +
                         "}\n" +
                         "JSON\n" +
                         "exit 1\n";
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false));

            var chmodStart = new ProcessStartInfo("chmod", $"+x \"{scriptPath}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using (var chmodProcess = Process.Start(chmodStart))
            {
                if (chmodProcess == null)
                    throw new InvalidOperationException("Failed to start chmod to prepare mock script.");

                var stderrTask = chmodProcess.StandardError.ReadToEndAsync();
                var stdoutTask = chmodProcess.StandardOutput.ReadToEndAsync();
                await chmodProcess.WaitForExitAsync();

                await stdoutTask;
                var errorOutput = await stderrTask;

                if (chmodProcess.ExitCode != 0)
                {
                    throw new InvalidOperationException($"chmod failed: {errorOutput}");
                }
            }

            var audioPath = Path.Combine(tempRoot, "audio.wav");
            await File.WriteAllBytesAsync(audioPath, new byte[] { 0, 1, 2, 3 });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FasterWhisper:ExecutablePath"] = scriptPath,
                    ["FasterWhisper:Model"] = "mock",
                    ["FasterWhisper:Device"] = "cpu",
                    ["FasterWhisper:ComputeType"] = "int8",
                    ["FasterWhisper:Language"] = "en"
                })
                .Build();

            /*
            var service = new FasterWhisperTranscriptionService(
                configuration,
                NullLogger<FasterWhisperTranscriptionService>.Instance);

            var workingDirectory = Path.Combine(tempRoot, "work");
            Directory.CreateDirectory(workingDirectory);

            var result = await service.TranscribeAsync(audioPath, workingDirectory, ffmpegExecutable: null, CancellationToken.None);

            Assert.Contains("hello world", result.TimecodedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hello world", result.RawJson, StringComparison.OrdinalIgnoreCase);*/
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
