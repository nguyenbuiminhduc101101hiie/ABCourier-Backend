using System.Net;
using System.Net.Mail;
using CourierApi.Options;
using Microsoft.Extensions.Options;

namespace CourierApi.Services;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options)
{
    private readonly SmtpOptions _smtp = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_smtp.Host) &&
        !string.IsNullOrWhiteSpace(_smtp.SenderEmail);

    public async Task SendHtmlAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMTP chưa được cấu hình trong appsettings (Smtp).");

        ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;
        try
        {
            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.EnableSsl,
                Credentials = string.IsNullOrWhiteSpace(_smtp.Username)
                    ? null
                    : new NetworkCredential(_smtp.Username, _smtp.Password),
            };

            var from = string.IsNullOrWhiteSpace(_smtp.FromName)
                ? new MailAddress(_smtp.SenderEmail)
                : new MailAddress(_smtp.SenderEmail, _smtp.FromName);
            using var message = new MailMessage
            {
                From = from,
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(toEmail);
            await client.SendMailAsync(message, ct);
        }
        finally
        {
            ServicePointManager.ServerCertificateValidationCallback = null;
        }
    }
}
