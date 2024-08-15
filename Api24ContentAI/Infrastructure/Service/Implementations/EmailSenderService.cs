using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class EmailSenderService : IEmailSenderService
    {
        private readonly EmailSettings _emailSettings;

        public EmailSenderService(IOptions<EmailSettings> emailSettings)
        {
            _emailSettings = emailSettings.Value;
        }

        public async Task SendEmailAsync(string email, string body, string subject, CancellationToken cancellationToken)
        {
            var mail = new MimeMessage();
            mail.From.Add(MailboxAddress.Parse(_emailSettings.Email));
            mail.To.Add(MailboxAddress.Parse(email));
            mail.Subject = subject;
            mail.Body = new TextPart(TextFormat.Html) { Text = body };

            using var smpt = new SmtpClient();
            await smpt.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.Port, SecureSocketOptions.StartTls, cancellationToken);
            await smpt.AuthenticateAsync(_emailSettings.Email, _emailSettings.Password, cancellationToken);
            await smpt.SendAsync(mail);
            await smpt.DisconnectAsync(true, cancellationToken);
        }

    }
}
