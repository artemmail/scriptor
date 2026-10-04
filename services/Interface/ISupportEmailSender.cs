using System.Net.Mail;

namespace YandexSpeech.services.Interface;

public interface ISupportEmailSender
{
    Task SendAsync(MailMessage message, CancellationToken cancellationToken);
}
