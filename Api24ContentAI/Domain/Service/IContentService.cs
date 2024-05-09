using Api24ContentAI.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IContentService
    {
        Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken);
    }
}
