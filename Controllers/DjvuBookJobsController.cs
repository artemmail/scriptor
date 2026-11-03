using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.Extensions;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.Controllers;

[ApiController]
[Route("api/djvu-books")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DjvuBookJobsController(MyDbContext db, ISubscriptionService subscriptions) : ControllerBase
{
    private const int BookCost = 10;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var jobs = await db.DjvuBookJobs.AsNoTracking().Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt).Take(100)
            .Select(j => new { j.Id, j.Title, j.FileName, j.Status, j.TotalBlocks, j.CompletedBlocks,
                j.CreatedAt, j.UpdatedAt, j.CompletedAt, j.Error })
            .ToListAsync(cancellationToken);
        var balance = await subscriptions.GetQuotaBalanceAsync(userId, cancellationToken);
        return Ok(new { jobs, remainingVideoCredits = balance.RemainingVideos, bookCost = BookCost });
    }

    [HttpPost]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Create([FromBody] CreateDjvuBookRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (request is null || !System.Text.RegularExpressions.Regex.IsMatch(request.FileName ?? "", @"\.djvu?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.FileName.Length > 260 ||
            request.Chapters is null || request.Chapters.Count is < 1 or > 1000 ||
            request.Blocks is null || request.Blocks.Count is < 1 or > 250 ||
            request.Chapters.Any(c => string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 200 || c.Depth is < 0 or > 10 || c.Page < 1) ||
            request.Blocks.Any(b => b.ChapterIndex < 0 || b.ChapterIndex >= request.Chapters.Count || string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > 8000) ||
            request.Blocks.Sum(b => b.Text.Length) > 1_300_000)
            return BadRequest(new { error = "Не удалось принять книгу: проверьте текстовый слой, оглавление и размер файла." });

        var balance = await subscriptions.GetQuotaBalanceAsync(userId, cancellationToken);
        if (balance.RemainingVideos < BookCost)
            return StatusCode(402, new { error = "Для конвертации книги нужно 10 видео-кредитов." });

        var now = DateTime.UtcNow;
        var job = new DjvuBookJob
        {
            Id = Guid.NewGuid(), UserId = userId, Title = request.Title.Trim(), FileName = request.FileName,
            TotalBlocks = request.Blocks.Count, CreatedAt = now, UpdatedAt = now,
            Chapters = request.Chapters.Select((c, i) => new DjvuBookChapter
            { Ordinal = i, Title = c.Title.Trim(), Depth = c.Depth, Page = c.Page }).ToList(),
            Blocks = request.Blocks.Select((b, i) => new DjvuBookBlock
            { Ordinal = i, ChapterIndex = b.ChapterIndex, Text = b.Text }).ToList()
        };
        db.DjvuBookJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new { job.Id, job.Status });
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var job = await db.DjvuBookJobs.FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);
        if (job is null) return NotFound();
        if (job.Status is not ("failed" or "awaiting_credits")) return Conflict(new { error = "Эту задачу сейчас нельзя повторить." });
        var balance = await subscriptions.GetQuotaBalanceAsync(userId, cancellationToken);
        if (balance.RemainingVideos < BookCost)
            return StatusCode(402, new { error = "Для конвертации книги нужно 10 видео-кредитов." });
        job.Status = "queued";
        job.Error = null;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Accepted(new { job.Id, job.Status });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var job = await db.DjvuBookJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);
        if (job is null) return NotFound();
        if (job.Status != "completed" || job.ResultXml is null) return Conflict(new { error = "FB2 ещё не готов." });
        var safeTitle = System.Text.RegularExpressions.Regex.Replace(job.Title, @"[<>:""/\\|?*\x00-\x1f]", "-");
        return File(Encoding.UTF8.GetBytes(job.ResultXml), "application/x-fictionbook+xml", safeTitle[..Math.Min(safeTitle.Length, 100)] + ".fb2");
    }
}

public sealed record CreateDjvuBookRequest(string FileName, string Title, List<DjvuBookChapterInput> Chapters, List<DjvuBookBlockInput> Blocks);
public sealed record DjvuBookChapterInput(string Title, int Page, int Depth);
public sealed record DjvuBookBlockInput(int ChapterIndex, string Text);
