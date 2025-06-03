using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationService(
        ILanguageService languageService, 
        IFileProcessorFactory fileProcessorFactory,
        ILogger<DocumentTranslationService> logger
        )
        : IDocumentTranslationService
    {
        private readonly IFileProcessorFactory _fileProcessorFactory = fileProcessorFactory ?? throw new ArgumentNullException(nameof(fileProcessorFactory));
        private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        public async Task<DocumentTranslationResult> TranslateDocumentWithTesseract(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Domain.Models.DocumentFormat outputFormat, 
            CancellationToken cancellationToken)
        {
            try
            {
                var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);
                if (targetLanguage == null)
                {
                    return new DocumentTranslationResult
                    {
                        Success = false,
                        ErrorMessage = "Target language not found"
                    };
                }

                var processor = fileProcessorFactory.GetProcessor(file.FileName);
                return await processor.TranslateWithTesseract(file, targetLanguage.Id, userId, outputFormat, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating document with Tesseract");
                return new DocumentTranslationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<DocumentTranslationResult> TranslateDocumentWithClaude(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Domain.Models.DocumentFormat outputFormat, 
            CancellationToken cancellationToken)
        {
            try
            {
                var targetLanguage = await languageService.GetById(targetLanguageId, cancellationToken);
                if (targetLanguage == null)
                {
                    return new DocumentTranslationResult
                    {
                        Success = false,
                        ErrorMessage = "Target language not found"
                    };
                }

                var processor = fileProcessorFactory.GetProcessor(file.FileName);
                return await processor.TranslateWithClaude(file, targetLanguage.Id, userId, outputFormat, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error translating document with Claude");
                return new DocumentTranslationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }


        public async Task<DocumentTranslationResult> TranslateSRTFiles(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Domain.Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken)
        {
            return await TranslateDocumentWithClaude(file, targetLanguageId, userId, outputFormat, cancellationToken);
        }

        
    }
}
