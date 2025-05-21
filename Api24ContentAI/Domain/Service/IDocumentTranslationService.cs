using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IDocumentTranslationService
    {
        
        Task<DocumentTranslationResult> TranslateDocument(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken);
        
        Task<DocumentTranslationResult> TranslateOcrJson(
            string ocrJsonContent, 
            int targetLanguageId, 
            string userId, 
            CancellationToken cancellationToken);
    }
}
