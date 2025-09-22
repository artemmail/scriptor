using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using YandexSpeech.models.DB; // DbContext + YoutubeCaptionText, YoutubeCaptionTask


public interface IDocumentGeneratorService
{
    Task<string> GeneratePdfFromMarkdownAsync(string id, string markdown);
    Task<string> GenerateWordFromMarkdownAsync(string id, string markdown);
    Task<string> GenerateBbcodeFromMarkdownAsync(string id, string markdown);

    /// <summary>
    /// Конвертирует JSON субтитров из таблицы YoutubeCaptionTexts.Caption (по taskId)
    /// в SRT и сохраняет во временный файл. Возвращает полный путь к .srt.
    /// Имя файла формируется по Title задачи: Title[.lang].srt.
    /// </summary>
    Task<string> GenerateSrtFromDbJsonAsync(string taskId, string? lang = null);

    /// <summary>
    /// HTML -> Markdown через pandoc. Возвращает текст markdown.
    /// </summary>
    Task<string> GenerateMarkdownFromHtmlAsync(string id, string html);
}

public class DocumentGeneratorService : IDocumentGeneratorService
{
    private readonly string _pandocExecutablePath;
    private readonly string _pandocWorkingDirectory;
    private readonly YandexSpeech.MyDbContext _db; // <-- добавлено

    public DocumentGeneratorService(IConfiguration configuration, YandexSpeech.MyDbContext db)
    {
        _pandocExecutablePath = configuration.GetValue<string>("PandocPath") ?? "pandoc";
        _pandocWorkingDirectory = configuration.GetValue<string>("PandocWorkingDirectory")
                                  ?? Directory.GetCurrentDirectory();
        _db = db;
    }

    public async Task<string> GeneratePdfFromMarkdownAsync(string id, string markdown)
    {
        var (_, outputPath) = await PrepareAndRunPandoc(
            id, markdown, "pdf", "--pdf-engine=lualatex");
        return outputPath;
    }

    public async Task<string> GenerateWordFromMarkdownAsync(string id, string markdown)
    {
        var (_, outputPath) = await PrepareAndRunPandoc(
            id, markdown, "docx", null);
        return outputPath;
    }

    public async Task<string> GenerateBbcodeFromMarkdownAsync(string id, string markdown)
    {
        string tempMdPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        await File.WriteAllTextAsync(tempMdPath, markdown, Encoding.UTF8);

        string outputPath = Path.Combine(Path.GetTempPath(), $"{id}.bbcode");
        string bbcodeWriterPath = Path.Combine(_pandocWorkingDirectory, "bbcode_phpbb.lua");

        var args = new StringBuilder();
        args.AppendFormat("\"{0}\" -o \"{1}\" --to=\"{2}\"", tempMdPath, outputPath, bbcodeWriterPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _pandocExecutablePath,
            Arguments = args.ToString(),
            WorkingDirectory = _pandocWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        string stdErr = await process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        File.Delete(tempMdPath);
        if (process.ExitCode != 0)
            throw new Exception($"Pandoc error (exit code {process.ExitCode}): {stdErr}");

        string bbcode = await File.ReadAllTextAsync(outputPath);
        File.Delete(outputPath);
        return bbcode;
    }

    // ======================== SRT из JSON (БД) ========================

    /// <summary>
    /// Берёт JSON субтитров из YoutubeCaptionTexts.Caption (по taskId), конвертит в SRT
    /// и сохраняет во временный файл. Имя файла: Title[.lang].srt (если Title пустой — taskId).
    /// Возвращает полный путь к файлу .srt.
    /// </summary>
    public async Task<string> GenerateSrtFromDbJsonAsync(string taskId, string? lang = null)
    {
        // 1) Достаём JSON субтитров
        var cap = await _db.YoutubeCaptionTexts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == taskId);

        if (cap == null || string.IsNullOrWhiteSpace(cap.Caption))
            throw new Exception("Субтитры для этой задачи не найдены в базе.");

        // 2) Достаём title задачи для имени файла
        var task = await _db.YoutubeCaptionTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId);

        var baseTitle = MakeSafeFileName(task?.Title ?? taskId);
        var fileName = !string.IsNullOrWhiteSpace(lang)
            ? $"{baseTitle}.{lang}.srt"
            : $"{baseTitle}.srt";

        // 3) Конвертируем JSON -> SRT (формат: массив с Text/Offset/Duration/Parts)
        string srt = ConvertJsonToSrt(cap.Caption);

        // 4) Сохраняем во временный файл (и возвращаем путь)
        var outPath = EnsureUniquePath(Path.Combine(Path.GetTempPath(), fileName));
        await File.WriteAllTextAsync(outPath, srt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return outPath;
    }

    // ======================== Pandoc общие ========================

    private async Task<(string tempMdPath, string outputPath)> PrepareAndRunPandoc(
        string id, string markdown, string format, string pdfEngineArgs)
    {
        string yamlHeader = @"
---
mainfont: ""Calibri""
sansfont: ""Calibri""
monofont: ""Consolas""
mathfont: ""Cambria Math""
geometry: a4paper, margin=0.5in
header-includes:
  - \usepackage{polyglossia}
  - \setdefaultlanguage{russian}
  - \setotherlanguage{english}
  - \newfontfamily\cyrillicfont{Calibri}
  - \newfontfamily\cyrillicfontsf{Calibri}
  - \newfontfamily\cyrillicfonttt{Consolas}
---
";

        string cleaned = PreprocessMathMinimal(markdown);
        string finalMd = yamlHeader + "\n" + cleaned;

        string tempMdPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        await File.WriteAllTextAsync(tempMdPath, finalMd, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        string ext = format.Equals("pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "docx";
        string outputPath = Path.Combine(Path.GetTempPath(), $"{id}.{ext}");

        var args = new StringBuilder();
        args.AppendFormat("\"{0}\" -o \"{1}\" --from=markdown+tex_math_dollars+tex_math_double_backslash+raw_tex",
            tempMdPath, outputPath);
        if (!string.IsNullOrEmpty(pdfEngineArgs))
            args.Append(' ').Append(pdfEngineArgs);

        var startInfo = new ProcessStartInfo
        {
            FileName = _pandocExecutablePath,
            Arguments = args.ToString(),
            WorkingDirectory = _pandocWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        string stdOut = await process.StandardOutput.ReadToEndAsync();
        string stdErr = await process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        File.Delete(tempMdPath);

        if (process.ExitCode != 0)
        {
            File.Delete(outputPath);
            throw new Exception($"Pandoc error (exit code {process.ExitCode}): {stdErr}");
        }

        return (tempMdPath, outputPath);
    }

    public async Task<string> GenerateMarkdownFromHtmlAsync(string id, string html)
    {
        string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
        await File.WriteAllTextAsync(tempHtmlPath, html, Encoding.UTF8);

        string outputPath = Path.Combine(Path.GetTempPath(), $"{id}.md");

        var args = new StringBuilder();
        args.AppendFormat("\"{0}\" -o \"{1}\" --from=html --to=markdown_strict+smart", tempHtmlPath, outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _pandocExecutablePath,
            Arguments = args.ToString(),
            WorkingDirectory = _pandocWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        string stdErr = await process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        File.Delete(tempHtmlPath);

        if (process.ExitCode != 0)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            throw new Exception($"Pandoc error (exit code {process.ExitCode}): {stdErr}");
        }

        string markdown = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
        return markdown;
    }

    private string PreprocessMathMinimal(string content)
    {
        const string DOLLAR_PLACEHOLDER = "§§§§§§";
        content = content.Replace("\\$", DOLLAR_PLACEHOLDER);

        content = Regex.Replace(
            content,
            @"\\\[((?:[\s\S]+?))\\\]",
            m => $"$${m.Groups[1].Value}$$",
            RegexOptions.Multiline);

        content = Regex.Replace(
            content,
            @"\\\(([\s\S]+?)\\\)",
            m => $"${m.Groups[1].Value}$",
            RegexOptions.Multiline);

        content = content.Replace(DOLLAR_PLACEHOLDER, "\\$");
        return content;
    }

    // ======================== Вспомогалки для SRT ========================

    private sealed class CaptionPartJson
    {
        public string? Text { get; set; }
        public string? Offset { get; set; }
    }

    private sealed class CaptionItemJson
    {
        public string? Text { get; set; }
        public string? Offset { get; set; }
        public string? Duration { get; set; }
        public List<CaptionPartJson>? Parts { get; set; }
    }

    private static string ConvertJsonToSrt(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        var items = JsonSerializer.Deserialize<List<CaptionItemJson>>(json, options) ?? new List<CaptionItemJson>();
        return BuildSrtFromJson(items);
    }

    // ЗАМЕНИ целиком метод BuildSrtFromJson

    private sealed class ParsedCaption
    {
        public TimeSpan Start { get; set; }
        public TimeSpan? End { get; set; }
        public string Text { get; set; } = "";
    }
    private static string BuildSrtFromJson(IEnumerable<CaptionItemJson> items)
    {
        var parsed = items
            .Select(i => new ParsedCaption
            {
                Start = ParseTs(i.Offset),
                End = string.IsNullOrWhiteSpace(i.Duration) ? (TimeSpan?)null : ParseTs(i.Offset) + ParseTs(i.Duration),
                Text = SanitizeText(i.Text, i.Parts)
            })
            .OrderBy(x => x.Start)
            .ToList();

        var sb = new StringBuilder();
        int idx = 1;

        for (int i = 0; i < parsed.Count; i++)
        {
            var cur = parsed[i];
            if (string.IsNullOrWhiteSpace(cur.Text) || cur.Text.Trim() == "\\n" || cur.Text.Trim() == "\n")
                continue;

            var start = cur.Start;
            var end = cur.End ?? FindNextStart(parsed, i) - TimeSpan.FromMilliseconds(1);
            if (end <= start) end = start + TimeSpan.FromMilliseconds(500);

            sb.AppendLine(idx.ToString());
            sb.AppendLine($"{Fmt(start)} --> {Fmt(end)}");
            sb.AppendLine(cur.Text);
            sb.AppendLine();
            idx++;
        }

        return sb.ToString();

        static TimeSpan FindNextStart(List<ParsedCaption> list, int i)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                var t = list[j].Text;
                if (!string.IsNullOrWhiteSpace(t) && t.Trim() != "\\n" && t.Trim() != "\n")
                    return list[j].Start;
            }
            return list[i].Start + TimeSpan.FromSeconds(2);
        }
    }


    private static TimeSpan ParseTs(string? s)
        => string.IsNullOrWhiteSpace(s) ? TimeSpan.Zero : TimeSpan.Parse(s);

    private static string SanitizeText(string? text, List<CaptionPartJson>? parts)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "\\n" || text.Trim() == "\n")
        {
            if (parts != null && parts.Count > 0)
                return string.Concat(parts.Select(p => p.Text ?? "")).Replace("\r", "").Trim();
            return "";
        }
        return text.Replace("\r", "").Trim();
    }

    private static string Fmt(TimeSpan t)
        => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";

    private static string MakeSafeFileName(string? s, int maxLen = 120)
    {
        if (string.IsNullOrWhiteSpace(s)) return "untitled";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(invalid.Contains(ch) ? '_' : ch);

        var cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };
        if (reserved.Contains(cleaned)) cleaned = "_" + cleaned;

        if (cleaned.Length > maxLen) cleaned = cleaned[..maxLen];
        cleaned = cleaned.Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
    }

    private static string EnsureUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        var candidate = path;
        int i = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(dir, $"{name} ({i++}){ext}");

        return candidate;
    }
}
