using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Threading;

namespace Api24ContentAI.Domain.Service
{
    public interface IUserContentService
    {
        Task<ContentAIResponse> SendRequest(UserContentAIRequest request, string userId, CancellationToken cancellationToken);
        Task<TranslateResponse> ChunkedTranslate(UserTranslateRequest request, string userId, CancellationToken cancellationToken);
        Task<CopyrightAIResponse> CopyrightAI(IFormFile file, UserCopyrightAIRequest request, string userId, CancellationToken cancellationToken);
        Task<EmailAIResponse> Email(UserEmailRequest request, string userId, CancellationToken cancellationToken);
        Task<VideoScriptAIResponse> VideoScript(IFormFile file, UserVideoScriptAIRequest request, string userId, CancellationToken cancellationToken);
        Task<TranslateResponse> EnhanceTranslate(UserTranslateEnhanceRequest request, string userId, CancellationToken cancellationToken);
    }
}
