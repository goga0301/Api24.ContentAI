using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IGeminiService
    {
        Task<GeminiResponse> SendRequest(GeminiRequest request, CancellationToken cancellationToken);
        Task<GeminiResponse> SendRequestWithFile(List<GeminiPart> parts, CancellationToken cancellationToken);

        Task<VerificationResult> VerifyResponseQuality(ClaudeRequest request, ClaudeResponse response, CancellationToken cancellationToken);

        Task<VerificationResult> VerifyTranslationBatch(List<KeyValuePair<int, string>> translations, CancellationToken cancellationToken);

        Task<VerificationResult> EvaluateTranslationQuality(string prompt, CancellationToken cancellationToken);

    }
} 
