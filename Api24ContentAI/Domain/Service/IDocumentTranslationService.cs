using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IDocumentTranslationService
    {
        
        Task<DocumentTranslationResult> TranslateDocumentWithTesseract(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken);

        Task<DocumentTranslationResult> TranslateDocumentWithClaude(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken
        );

        Task<DocumentTranslationResult> TranslateSRTFiles(
            IFormFile file,
            int targetLanguageId,
            string userId,
            Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken
        );

    }
}
