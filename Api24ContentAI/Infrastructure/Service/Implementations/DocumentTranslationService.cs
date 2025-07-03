using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

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

                var processor = _fileProcessorFactory.GetProcessor(file.FileName);
                return await processor.TranslateWithTesseract(file, targetLanguage.Id, userId, outputFormat, cancellationToken);
            }
            catch (Exception ex)
            {
                var errorMessage = HandleDocumentProcessingError(ex, "document translation", file.FileName);
                logger.LogError(ex, "Error translating document with Tesseract. File: {FileName}, User: {UserId}", file.FileName, userId);
                
                return new DocumentTranslationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                };
            }
        }
        
        public async Task<DocumentTranslationResult> TranslateDocumentWithClaude(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Domain.Models.DocumentFormat outputFormat,
            AIModel model,
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

                var processor = _fileProcessorFactory.GetProcessor(file.FileName);
                return await processor.TranslateWithClaude(file, targetLanguage.Id, userId, outputFormat, model, cancellationToken);
            }
            catch (Exception ex)
            {
                var errorMessage = HandleDocumentProcessingError(ex, "AI document translation", file.FileName);
                logger.LogError(ex, "Error translating document with Claude. File: {FileName}, User: {UserId}", file.FileName, userId);
                
                return new DocumentTranslationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
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
            try
            {
                return await TranslateDocumentWithClaude(file, targetLanguageId, userId, outputFormat, AIModel.Claude4Sonnet, cancellationToken);
            }
            catch (Exception ex)
            {
                var errorMessage = HandleDocumentProcessingError(ex, "SRT file translation", file.FileName);
                logger.LogError(ex, "Error translating SRT file. File: {FileName}, User: {UserId}", file.FileName, userId);
                
                return new DocumentTranslationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                };
            }
        }

        private string HandleDocumentProcessingError(Exception ex, string operationType, string fileName)
        {
            logger.LogError(ex, "Document processing error for {OperationType}. File: {FileName}", operationType, fileName);

            return ex switch
            {
                HttpRequestException httpEx when httpEx.Message.Contains("timeout") =>
                    $"AI service is temporarily unavailable. Your {operationType} did not complete due to a timeout. Please try again later.",

                HttpRequestException httpEx when httpEx.Message.Contains("503") || httpEx.Message.Contains("502") || httpEx.Message.Contains("500") =>
                    $"AI service is temporarily unavailable. Your {operationType} did not complete. Please try again in a few minutes.",

                HttpRequestException httpEx when httpEx.Message.Contains("429") =>
                    $"AI service is currently overloaded. Your {operationType} did not complete. Please wait a few minutes and try again.",

                HttpRequestException httpEx when httpEx.Message.Contains("401") || httpEx.Message.Contains("403") =>
                    $"AI service authentication problem. Your {operationType} did not complete. Please contact the support team.",

                HttpRequestException =>
                    $"AI service communication problem. Your {operationType} did not complete. Please check your internet connection and try again.",

                OperationCanceledException =>
                    $"Your {operationType} was cancelled or timed out. Please try again.",

                var fileEx when fileEx.Message.Contains("file") && fileEx.Message.Contains("size") =>
                    $"File '{fileName}' is too large for {operationType}. Please use a smaller file (max 100MB).",

                var fileEx when fileEx.Message.Contains("file") && (fileEx.Message.Contains("format") || fileEx.Message.Contains("extension")) =>
                    $"File '{fileName}' format is not supported for {operationType}. Supported formats: PDF, DOCX, DOC, TXT, PNG, JPG, SRT.",

                var fileEx when fileEx.Message.Contains("empty") || fileEx.Message.Contains("no content") =>
                    $"File '{fileName}' is empty or does not contain translatable text.",

                var contentEx when contentEx.Message.Contains("content") && contentEx.Message.Contains("large") =>
                    $"File '{fileName}' contains too much text for {operationType}. Please split it into smaller files.",

                var ocrEx when ocrEx.Message.Contains("OCR") || ocrEx.Message.Contains("tesseract") =>
                    $"Text extraction from file '{fileName}' failed. Please ensure the file contains text to be translated.",

                OutOfMemoryException =>
                    $"File '{fileName}' is too large for processing. Please use a smaller file.",

                var quotaEx when quotaEx.Message.Contains("quota") || quotaEx.Message.Contains("rate") || quotaEx.Message.Contains("limit") =>
                    $"AI service limit exceeded. Your {operationType} did not complete. Please try again in a few hours.",

                var jsonEx when jsonEx.Message.Contains("JSON") || jsonEx.Message.Contains("Deserialize") =>
                    $"AI service returned incorrect response. Your {operationType} did not complete. Please try again.",

                NotSupportedException notSupportedEx =>
                    $"Operation '{operationType}' is not supported for file '{fileName}'. Please change the file type or use another method.",

                var authEx when authEx.Message.Contains("API") && authEx.Message.Contains("key") =>
                    $"AI service configuration problem. Your {operationType} did not complete. Please contact the support team.",

                ArgumentException argEx =>
                    $"Invalid parameters for {operationType}: {GetSafeErrorMessage(argEx.Message)}",

                _ => $"Your {operationType} did not complete due to an unhandled exception. File: '{fileName}'. Please try again or contact the support team."
            };
        }

        private static string GetSafeErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return "Unknown error";

            var safeMessage = errorMessage
                .Replace("API key", "***")
                .Replace("token", "***")
                .Replace("password", "***")
                .Replace("secret", "***");

            if (safeMessage.Length > 200)
                safeMessage = safeMessage.Substring(0, 200) + "...";

            return safeMessage;
        }
    }
}
