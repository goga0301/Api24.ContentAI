using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IDocumentSuggestionService
    {
        Task<List<TranslationSuggestion>> GenerateSuggestions(
            string originalContent, 
            string translatedContent, 
            int targetLanguageId, 
            CancellationToken cancellationToken,
            List<TranslationSuggestion> previousSuggestions = null,
            AIModel? model = null);

        Task<ApplySuggestionResponse> ApplySuggestion(
            ApplySuggestionRequest request, 
            CancellationToken cancellationToken);
    }
} 