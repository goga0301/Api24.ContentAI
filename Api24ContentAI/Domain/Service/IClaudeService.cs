using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IClaudeService
    {
        Task<ClaudeResponse> SendRequest(ClaudeRequest request, CancellationToken cancellationToken);
        Task<ClaudeResponse> SendRequestWithFile(ClaudeRequestWithFile request, CancellationToken cancellationToken);
    }
}
