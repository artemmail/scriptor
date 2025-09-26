using System.Diagnostics;
using System.Text;

namespace YandexSpeech.services.Whisper
{
    public sealed class FasterWhisperTranscriptionService : IWhisperTranscriptionService
    {
        private readonly ILogger<FasterWhisperTranscriptionService> _logger;
        private readonly string _executableSetting;
        private readonly string _model;
        private readonly string _device;
        private readonly string _computeType;
        private readonly string _language;

        // === Диагностика/артефакты при падении ===
        private const bool KeepFailedOutput = true; // не удалять папку вывода при ошибке
        private const int PreviewLogLines = 50;     // сколько строк STDOUT/STDERR показать в исключении

        public FasterWhisperTranscriptionService(
            IConfiguration configuration,
            ILogger<FasterWhisperTranscriptionService> logger)
        {
            _logger = logger;

            var section = configuration.GetSection("FasterWhisper");
            _executableSetting = section.GetValue<string>("ExecutablePath") ?? "faster-whisper";
            _model = section.GetValue<string>("Model") ?? configuration.GetValue<string>("Whisper:Model") ?? "medium";
            _device = section.GetValue<string>("Device") ?? configuration.GetValue<string>("Whisper:Device") ?? "cpu";
            // CPU-safe по умолчанию; для CUDA нормализуем далее
            _computeType = section.GetValue<string>("ComputeType") ?? "int8";
            _language = section.GetValue<string>("Language")
                ?? configuration.GetValue<string>("Whisper:Language")
                ?? "ru";
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
                workingDirectory = Path.Combine(Path.GetTempPath(), "openai-transcriptions");

            Directory.CreateDirectory(workingDirectory);

            var outputDirectory = Path.Combine(workingDirectory, $"faster-whisper-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDirectory);

            var startInfo = BuildProcessStartInfo(audioPath, outputDirectory, ffmpegExecutable);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                _logger.LogInformation("Starting transcription: {File} -> {OutDir}", audioPath, outputDirectory);

                try
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start transcription process.");
                }
                catch (System.ComponentModel.Win32Exception w32)
                {
                    _logger.LogError(w32, "Failed to start process. FileName={FileName}", startInfo.FileName);
                    throw;
                }

                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                var standardError = await stderrTask;
                var standardOutput = await stdoutTask;

                // Всегда сохраняем логи в файлы
                var preview = SaveAndPreviewLogs(outputDirectory, standardOutput, standardError);

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Transcription failed. ExitCode={Code}\n{Preview}",
                        process.ExitCode, preview);

                    if (KeepFailedOutput)
                        _logger.LogWarning("Keeping failed output directory: {OutDir}", outputDirectory);

                    throw new InvalidOperationException(
                        $"FasterWhisper transcription failed (exit code {process.ExitCode}). " +
                        $"See {Path.Combine(outputDirectory, "stdout.log")} and {Path.Combine(outputDirectory, "stderr.log")}.\n\n" +
                        preview
                    );
                }

                var transcriptFile = WhisperTranscriptionHelper.FindFirstJsonFile(outputDirectory);
                if (transcriptFile == null)
                {
                    _logger.LogError("No JSON output produced.\n{Preview}", preview);

                    if (KeepFailedOutput)
                        _logger.LogWarning("Keeping output directory without JSON: {OutDir}", outputDirectory);

                    throw new InvalidOperationException(
                        "FasterWhisper transcription did not produce a JSON file.\n\n" + preview
                    );
                }

                var transcription = await File.ReadAllTextAsync(transcriptFile, cancellationToken);
                if (string.IsNullOrWhiteSpace(transcription))
                    throw new InvalidOperationException("FasterWhisper transcription result is empty.");

                var parsed = WhisperTranscriptionHelper.Parse(transcription);
                if (parsed?.Segments == null || parsed.Segments.Count == 0)
                    throw new InvalidOperationException("FasterWhisper transcription does not contain segments.");

                var timecodedText = WhisperTranscriptionHelper.BuildTimecodedText(parsed);

                return new WhisperTranscriptionResult
                {
                    TimecodedText = timecodedText,
                    RawJson = transcription
                };
            }
            finally
            {
                if (!KeepFailedOutput && Directory.Exists(outputDirectory))
                {
                    try { Directory.Delete(outputDirectory, true); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove output {OutDir}", outputDirectory); }
                }
            }
        }

        // -------------------- helpers --------------------

        private ProcessStartInfo BuildProcessStartInfo(string audioPath, string outputDirectory, string? ffmpegExecutable)
        {
            var safeComputeType = NormalizeCt2(_device, _computeType);

            var exe = ResolveExecutable();
            var useCli = File.Exists(exe) || IsInPath(exe);

            if (useCli)
            {
                // === ветка CLI faster-whisper ===
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                EnsureEnvironmentEncoding(psi);
                ConfigureFfmpegEnvironment(psi, ffmpegExecutable);

                psi.ArgumentList.Add(audioPath);
                psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(_model);
                psi.ArgumentList.Add("--device"); psi.ArgumentList.Add(_device);
                psi.ArgumentList.Add("--compute_type"); psi.ArgumentList.Add(safeComputeType);
                psi.ArgumentList.Add("--output_dir"); psi.ArgumentList.Add(outputDirectory);
                psi.ArgumentList.Add("--output_format"); psi.ArgumentList.Add("json");
                psi.ArgumentList.Add("--word_timestamps"); psi.ArgumentList.Add("True");
                psi.ArgumentList.Add("--language"); psi.ArgumentList.Add(_language);

                return psi;
            }
            else
            {
                // === фолбэк: Python-раннер, использующий библиотеку faster_whisper ===
                var scriptPath = WriteInlinePythonRunner(outputDirectory);

                var psi = new ProcessStartInfo
                {
                    FileName = " C:\\Python312\\python.exe",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                EnsureEnvironmentEncoding(psi);
                ConfigureFfmpegEnvironment(psi, ffmpegExecutable);

                // Безопасные ENV для CTranslate2 на Windows Server
                psi.Environment["CT2_CUDA_DISABLE_CUDNN"] = "1";
                psi.Environment["CT2_CUDA_USE_TF32"] = "1";
                psi.Environment["CT2_VERBOSE"] = "1";
                psi.Environment["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1";

                psi.ArgumentList.Add(scriptPath);
                psi.ArgumentList.Add(audioPath);
                psi.ArgumentList.Add(_model);
                psi.ArgumentList.Add(_device);
                psi.ArgumentList.Add(safeComputeType);
                psi.ArgumentList.Add(_language);
                psi.ArgumentList.Add(outputDirectory);

                return psi;
            }
        }

        private static string NormalizeCt2(string device, string computeType)
        {
            var d = (device ?? "cpu").Trim().ToLowerInvariant();
            var ct = (computeType ?? "").Trim().ToLowerInvariant();

            if (d == "cuda" || d == "gpu" || d.StartsWith("cuda:"))
            {
                // допустимо на CUDA:
                //   float16 | int8_float16 | float32
                return ct switch
                {
                    "float16" => "float16",
                    "int8_float16" => "int8_float16",
                    "float32" => "float32",
                    _ => "float16"
                };
            }
            else
            {
                // CPU: int8 | float32 (float16 допускаем — будет проигнорирован)
                return ct switch
                {
                    "int8" => "int8",
                    "float32" => "float32",
                    "float16" => "float16",
                    _ => "int8"
                };
            }
        }

        private string ResolveExecutable()
        {
            if (File.Exists(_executableSetting))
                return _executableSetting;

            if (Directory.Exists(_executableSetting))
            {
                var executableName = OperatingSystem.IsWindows() ? "faster-whisper.exe" : "faster-whisper";
                var candidate = Path.Combine(_executableSetting, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return _executableSetting; // имя в PATH
        }

        private static bool IsInPath(string nameOrExe)
        {
            if (string.IsNullOrWhiteSpace(nameOrExe)) return false;

            // если содержит путь/разделители — не PATH-режим
            if (nameOrExe.Contains(Path.DirectorySeparatorChar) || nameOrExe.Contains(Path.AltDirectorySeparatorChar))
                return false;

            try
            {
                var envName = OperatingSystem.IsWindows() ? "Path" : "PATH";
                var values = (Environment.GetEnvironmentVariable(envName) ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

                var candidates = OperatingSystem.IsWindows()
                    ? new[] { nameOrExe, nameOrExe + ".exe", nameOrExe + ".cmd", nameOrExe + ".bat" }
                    : new[] { nameOrExe };

                foreach (var dir in values)
                {
                    foreach (var c in candidates)
                    {
                        var full = Path.Combine(dir, c);
                        if (File.Exists(full))
                            return true;
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static void EnsureEnvironmentEncoding(ProcessStartInfo startInfo)
        {
            if (!startInfo.Environment.ContainsKey("PYTHONIOENCODING"))
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            if (OperatingSystem.IsWindows())
                startInfo.Environment["PYTHONUTF8"] = "1";

            startInfo.StandardErrorEncoding = Encoding.UTF8;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
        }

        private static void ConfigureFfmpegEnvironment(ProcessStartInfo startInfo, string? ffmpegExecutable)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExecutable))
                return;

            startInfo.Environment["FFMPEG_BINARY"] = ffmpegExecutable;

            var ffmpegDirectory = Path.GetDirectoryName(ffmpegExecutable);
            if (string.IsNullOrWhiteSpace(ffmpegDirectory) || !Directory.Exists(ffmpegDirectory))
                return;

            var pathVariableName = OperatingSystem.IsWindows() ? "Path" : "PATH";
            if (!startInfo.Environment.TryGetValue(pathVariableName, out var currentPath) || string.IsNullOrWhiteSpace(currentPath))
                currentPath = Environment.GetEnvironmentVariable(pathVariableName);

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                startInfo.Environment[pathVariableName] = ffmpegDirectory;
                return;
            }

            var segments = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var exists = segments.Any(p => string.Equals(
                p, ffmpegDirectory,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            if (!exists)
                startInfo.Environment[pathVariableName] = string.Concat(ffmpegDirectory, Path.PathSeparator, currentPath);
        }

        private static string SaveAndPreviewLogs(string outputDirectory, string stdOut, string stdErr)
        {
            try
            {
                var so = Path.Combine(outputDirectory, "stdout.log");
                var se = Path.Combine(outputDirectory, "stderr.log");
                File.WriteAllText(so, stdOut ?? string.Empty, new UTF8Encoding(false));
                File.WriteAllText(se, stdErr ?? string.Empty, new UTF8Encoding(false));
            }
            catch { /* ignore file errors */ }

            static string Head(string s, int n)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var lines = s.Replace("\r\n", "\n").Split('\n');
                return string.Join(Environment.NewLine, lines.Take(n));
            }

            var headOut = Head(stdOut, PreviewLogLines);
            var headErr = Head(stdErr, PreviewLogLines);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(headOut))
            {
                sb.AppendLine("STDOUT (preview):");
                sb.AppendLine(headOut);
            }
            if (!string.IsNullOrWhiteSpace(headErr))
            {
                sb.AppendLine("STDERR (preview):");
                sb.AppendLine(headErr);
            }
            return sb.ToString();
        }

        private string WriteInlinePythonRunner(string outputDirectory)
        {
            var scriptPath = Path.Combine(outputDirectory, "fw_runner.py");
            var py = @"
import sys, json, os, traceback
from pathlib import Path
print('fw_runner: PY', sys.version)
print('fw_runner: CT2_CUDA_DISABLE_CUDNN=', os.getenv('CT2_CUDA_DISABLE_CUDNN'))
print('fw_runner: CT2_CUDA_USE_TF32=', os.getenv('CT2_CUDA_USE_TF32'))
try:
    from faster_whisper import WhisperModel
except Exception as e:
    print('IMPORT ERROR faster_whisper:', e, file=sys.stderr)
    traceback.print_exc()
    sys.exit(3)

def main():
    if len(sys.argv) != 7:
        print('usage: fw_runner.py <audio> <model> <device> <compute_type> <language> <output_dir>', file=sys.stderr)
        sys.exit(2)
    audio, model, device, compute_type, language, out_dir = sys.argv[1:]
    print('fw_runner args:', audio, model, device, compute_type, language, out_dir)

    out = Path(out_dir); out.mkdir(parents=True, exist_ok=True)

    try:
        m = WhisperModel(model, device=device, compute_type=compute_type)
    except Exception as e:
        print('MODEL INIT ERROR:', e, file=sys.stderr)
        traceback.print_exc()
        sys.exit(4)

    try:
        segments, info = m.transcribe(
            audio,
            language=language if language and language.lower() != 'auto' else None,
            word_timestamps=True,
            vad_filter=False,         # без ORT/VAD
            beam_size=5,
            temperature=0.0,
            without_timestamps=False  # нужны таймкоды сегментов
        )
        data = {
            'language': getattr(info, 'language', None),
            'language_probability': float(getattr(info, 'language_probability', 0.0) or 0.0),
            'segments': []
        }
        for seg in segments:
            item = {
                'start': float(getattr(seg, 'start', 0.0) or 0.0),
                'end': float(getattr(seg, 'end', 0.0) or 0.0),
                'text': (getattr(seg, 'text', '') or '').strip(),
                'words': []
            }
            for w in getattr(seg, 'words', []) or []:
                item['words'].append({
                    'start': float(getattr(w, 'start', 0.0) or 0.0),
                    'end': float(getattr(w, 'end', 0.0) or 0.0),
                    'word': (getattr(w, 'word', '') or '')
                })
            data['segments'].append(item)

        out_file = out / 'transcript.json'
        out_file.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        print('fw_runner OK ->', str(out_file))
    except Exception as e:
        print('INFERENCE ERROR:', e, file=sys.stderr)
        traceback.print_exc()
        sys.exit(5)

if __name__ == '__main__':
    main()
";
            File.WriteAllText(scriptPath, py, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return scriptPath;
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
                _logger.LogWarning(ex, "Failed to remove FasterWhisper output directory {OutputDirectory}", directory);
            }
        }
    }
}
