using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.services;

public sealed class SmtpSupportEmailSender(IOptions<SmtpOptions> options) : ISupportEmailSender
{
    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(smtp.UserName, smtp.Password),
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, timeout.Token);
    }
}
