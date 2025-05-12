using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IDocumentTranslationService
    {
        Task<DocumentConversionResult> ConvertToMarkdown(IFormFile file, CancellationToken cancellationToken);
        
        Task<DocumentTranslationResult> TranslateMarkdown(
            string markdownContent, 
            int targetLanguageId, 
            string userId, 
            CancellationToken cancellationToken);
        
        Task<DocumentConversionResult> ConvertFromMarkdown(
            string markdownContent, 
            Models.DocumentFormat outputFormat, 
            CancellationToken cancellationToken);
        
        Task<DocumentTranslationResult> TranslateDocument(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken);
    }
}