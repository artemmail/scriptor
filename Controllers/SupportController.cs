using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SupportController(
    UserManager<ApplicationUser> userManager,
    IOptions<SmtpOptions> options,
    ISupportEmailSender emailSender,
    ILogger<SupportController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SupportFormModel.MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = SupportFormModel.MaxRequestSize, ValueLengthLimit = 40000)]
    public async Task<IActionResult> PostAsync([FromForm] SupportFormModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized();

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), validationResults, true))
            return BadRequest(new { message = "Заполните тип обращения, тему (до 200 символов) и текст (до 10 000 символов)." });

        if (model.UploadedFile is { Length: > SupportFormModel.MaxFileSize })
            return BadRequest(new { message = "Размер вложения не должен превышать 10 МБ." });

        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.UserName)
            || string.IsNullOrWhiteSpace(smtp.Password) || smtp.Port is < 1 or > 65535)
        {
            logger.LogError("Support SMTP settings are not configured");
            return StatusCode(503, new { message = "Отправка обращений временно недоступна. Попробуйте позже." });
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(string.IsNullOrWhiteSpace(smtp.FromEmail) ? smtp.UserName : smtp.FromEmail, smtp.FromName),
                Subject = $"[YouScriptor] {model.MessageType.Trim()} ({model.Header.Trim()})",
                Body = $"{model.Text.Trim()}\n\nОт пользователя: {user.DisplayName} ({user.UserName})\nEmail: {user.Email}\nID: {user.Id}",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            message.To.Add(new MailAddress(smtp.SupportEmail));


       


            if (MailAddress.TryCreate(user.Email, out var replyTo))
                message.ReplyToList.Add(replyTo);

            if (model.UploadedFile is { Length: > 0 } file)
            {
                var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
                name = new string(name.Where(c => !char.IsControl(c)).ToArray());
                message.Attachments.Add(new Attachment(file.OpenReadStream(),
                    string.IsNullOrWhiteSpace(name) ? "attachment" : name, "application/octet-stream"));
            }

            await emailSender.SendAsync(message, cancellationToken);
            return Ok(new { message = "Сообщение отправлено в поддержку." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException or OperationCanceledException or IOException or ArgumentException)
        {
            logger.LogError(ex, "Error sending support email for user {UserId}", user.Id);
            return StatusCode(503, new { message = "Не удалось отправить сообщение. пишите на почту ruticker@gmail.com" });
        }
    }
}
