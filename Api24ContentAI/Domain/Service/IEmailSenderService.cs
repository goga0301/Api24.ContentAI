using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IEmailSenderService
    {
        Task<string> SendEmailAsync(string email, CancellationToken cancellationToken);
    }
}
