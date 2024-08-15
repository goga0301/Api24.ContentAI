using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IEmailSenderService
    {
        Task SendEmailAsync(string email, string body, string subject, CancellationToken cancellationToken);
    }
}
