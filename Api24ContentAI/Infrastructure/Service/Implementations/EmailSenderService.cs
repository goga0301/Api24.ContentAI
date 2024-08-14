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

        public async Task<string> SendEmailAsync(string email, CancellationToken cancellationToken)
        {
            var code = GenerateRandomCode();
            var mail = new MimeMessage();
            mail.From.Add(MailboxAddress.Parse(_emailSettings.Email));
            mail.To.Add(MailboxAddress.Parse(email));
            mail.Subject = "Verification Code";
            mail.Body = new TextPart(TextFormat.Html)
            {
                Text = $"<b>This is your verification code: {code}</b>"
            };

            using var smpt = new SmtpClient();
            smpt.Connect(_emailSettings.SmtpServer, _emailSettings.Port, SecureSocketOptions.StartTls, cancellationToken);
            smpt.Authenticate(_emailSettings.Email, _emailSettings.Password, cancellationToken);
            smpt.Send(mail);
            smpt.Disconnect(true, cancellationToken);

            return code;
        }

        public string GenerateRandomCode()
        {
            Random random = new Random();
            int code = random.Next(100000, 999999); // Generates a random 6-digit number
            return code.ToString();
        }

    }
}
