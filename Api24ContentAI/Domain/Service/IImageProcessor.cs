using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service;

public interface IImageProcessor : IFileProcessor
{
    Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        CancellationToken cancellationToken);

    Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        AIModel model, CancellationToken cancellationToken);
} 