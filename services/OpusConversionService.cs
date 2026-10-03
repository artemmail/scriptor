using System.Diagnostics;

namespace YandexSpeech.Services;

public sealed class OpusConversionService
{
    private readonly string _ffmpegPath;

    public OpusConversionService(IConfiguration configuration)
    {
        _ffmpegPath = configuration["FfmpegExecutable"]
            ?? configuration["FfmpegExePath"]
            ?? configuration["Telegram:FfmpegExecutable"]
            ?? "ffmpeg";
    }

    public async Task ConvertToOpusAsync(string inputFile, string outputFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-y", "-i", inputFile, "-vn", "-ac", "1", "-ar", "48000",
            "-c:a", "libopus", "-b:a", "64k", outputFile
        })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var started = false;
        try
        {
            process.Start();
            started = true;
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await errorTask;
            await outputTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {error}");
        }
        catch
        {
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            throw;
        }
    }
}
