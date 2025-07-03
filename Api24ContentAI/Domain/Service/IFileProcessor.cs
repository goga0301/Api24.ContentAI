

using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Service
{
    public interface IFileProcessor
    {
        bool CanProcess(string fileExtension);
        
        Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId,
            Models.DocumentFormat outputFormat, CancellationToken cancellationToken);
        Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId,
            Models.DocumentFormat outputFormat, AIModel model, CancellationToken cancellationToken);
    }
}

