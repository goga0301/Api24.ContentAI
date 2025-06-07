using Api24ContentAI.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IClaudeService
    {
        Task<ClaudeResponse> SendRequest(ClaudeRequest request, CancellationToken cancellationToken);
        Task<ClaudeResponse> SendRequestWithFile(ClaudeRequestWithFile request, CancellationToken cancellationToken);
        Task<ClaudeResponse> SendRequestWithCachedPrompt(ClaudeRequestWithFile request, CancellationToken cancellationToken);
    }
}
