using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Threading;

namespace Api24ContentAI.Domain.Service
{
    public interface IUserContentService
    {
        Task<CopyrightAIResponse> BasicMessage(BasicMessageRequest request, CancellationToken cancellationToken);
        Task<ContentAIResponse> SendRequest(UserContentAIRequest request, string userId, CancellationToken cancellationToken);
        Task<TranslateResponse> ChunkedTranslate(UserTranslateRequestWithChunks request, string userId, CancellationToken cancellationToken);
        Task<CopyrightAIResponse> CopyrightAI(IFormFile file, UserCopyrightAIRequest request, string userId, CancellationToken cancellationToken);
        Task<EmailAIResponse> Email(UserEmailRequest request, string userId, CancellationToken cancellationToken);
        Task<VideoScriptAIResponse> VideoScript(IFormFile file, UserVideoScriptAIRequest request, string userId, CancellationToken cancellationToken);
        Task<EnhanceTranslateResponse> EnhanceTranslate(UserTranslateEnhanceRequest request, string userId, CancellationToken cancellationToken);
        Task<string> TestTranslateTextAsync(IFormFile file, CancellationToken cancellationToken);
    }
}
