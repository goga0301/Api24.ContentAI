using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;

namespace Api24ContentAI.Domain.Service;

public interface IGptService
{
    Task<VerificationResult> VerifyResponseQuality(ClaudeRequest request, ClaudeResponse response, CancellationToken cancellationToken);
    
    Task<VerificationResult> VerifyTranslationBatch(List<KeyValuePair<int, string>> translations, CancellationToken cancellationToken);
    
    Task<VerificationResult> EvaluateTranslationQuality(string prompt, CancellationToken cancellationToken);
}
