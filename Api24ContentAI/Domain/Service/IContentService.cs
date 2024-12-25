using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IContentService
    {
        Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken);
        Task<TranslateResponse> Translate(TranslateRequest request, CancellationToken cancellationToken);
        Task<TranslateResponse> EnhanceTranslate(EnhanceTranslateRequest request, CancellationToken cancellationToken);
        Task<CopyrightAIResponse> CopyrightAI(IFormFile file, CopyrightAIRequest request, CancellationToken cancellationToken);
        Task<VideoScriptAIResponse> VideoScript(IFormFile file, VideoScriptAIRequest request, CancellationToken cancellationToken);
        Task<LawyerResponse> Lawyer(LawyerRequest request, CancellationToken cancellationToken);
    }
}
