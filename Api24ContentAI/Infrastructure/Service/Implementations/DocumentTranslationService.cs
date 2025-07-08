using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Entities;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationService(
        ILanguageService languageService, 
        IFileProcessorFactory fileProcessorFactory,
        IUserRepository userRepository,
        IUserRequestLogService requestLogService,
        ILogger<DocumentTranslationService> logger
        )
        : IDocumentTranslationService
    {
        private readonly IFileProcessorFactory _fileProcessorFactory = fileProcessorFactory ?? throw new ArgumentNullException(nameof(fileProcessorFactory));
        private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        private readonly IUserRepository _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        private readonly IUserRequestLogService _requestLogService = requestLogService ?? throw new ArgumentNullException(nameof(requestLogService));

        public async Task<DocumentTranslationResult> TranslateDocumentWithTesseract(
            IFormFile file, 
            int targetLanguageId, 
            string userId, 
            Domain.Models.DocumentFormat outputFormat, 
            CancellationToken cancellationToken)
        {
            try
            {
                var pageCount = await GetDocumentPageCount(file, cancellationToken);
                var requestPrice = CalculateDocumentTranslationPrice(pageCount);
                
                var balanceCheckResult = await CheckUserBalance(userId, requestPrice, cancellationToken);
                if (!balanceCheckResult.HasSufficientBalance)
                {
                    return new DocumentTranslationResult
                    {
                        Success = false,
                        ErrorMessage = balanceCheckResult.ErrorMessage
                    };
                }

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
                var result = await processor.TranslateWithTesseract(file, targetLanguage.Id, userId, outputFormat, cancellationToken);

                if (result.Success)
                {
                    await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);
                    
                    await LogDocumentTranslationRequest(userId, file.FileName, targetLanguageId, outputFormat, 
                        result, RequestType.DocumentTranslationTesseract, pageCount, requestPrice, cancellationToken);
                    
                    _logger.LogInformation("Document translation with Tesseract completed successfully. User: {UserId}, File: {FileName}, Pages: {PageCount}, Price: {Price}", 
                        userId, file.FileName, pageCount, requestPrice);
                }

                return result;
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
                var pageCount = await GetDocumentPageCount(file, cancellationToken);
                var requestPrice = CalculateDocumentTranslationPrice(pageCount);
                
                var balanceCheckResult = await CheckUserBalance(userId, requestPrice, cancellationToken);
                if (!balanceCheckResult.HasSufficientBalance)
                {
                    return new DocumentTranslationResult
                    {
                        Success = false,
                        ErrorMessage = balanceCheckResult.ErrorMessage
                    };
                }

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
                var result = await processor.TranslateWithClaude(file, targetLanguage.Id, userId, outputFormat, model, cancellationToken);

                if (result.Success)
                {
                    await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);
                    
                    await LogDocumentTranslationRequest(userId, file.FileName, targetLanguageId, outputFormat, 
                        result, RequestType.DocumentTranslationClaude, pageCount, requestPrice, cancellationToken);
                    
                    _logger.LogInformation("Document translation with Claude completed successfully. User: {UserId}, File: {FileName}, Pages: {PageCount}, Price: {Price}", 
                        userId, file.FileName, pageCount, requestPrice);
                }

                return result;
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
                var pageCount = 1;
                var requestPrice = CalculateDocumentTranslationPrice(pageCount);
                
                var balanceCheckResult = await CheckUserBalance(userId, requestPrice, cancellationToken);
                if (!balanceCheckResult.HasSufficientBalance)
                {
                    return new DocumentTranslationResult
                    {
                        Success = false,
                        ErrorMessage = balanceCheckResult.ErrorMessage
                    };
                }

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
                var result = await processor.TranslateWithClaude(file, targetLanguage.Id, userId, outputFormat, AIModel.Claude4Sonnet, cancellationToken);

                // if translated then deduct price
                if (result.Success)
                {
                    await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);
                    
                    await LogDocumentTranslationRequest(userId, file.FileName, targetLanguageId, outputFormat, 
                        result, RequestType.DocumentTranslationSRT, pageCount, requestPrice, cancellationToken);
                    
                    _logger.LogInformation("SRT file translation completed successfully. User: {UserId}, File: {FileName}, Price: {Price}", 
                        userId, file.FileName, requestPrice);
                }

                return result;
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

        private async Task<(bool HasSufficientBalance, string ErrorMessage)> CheckUserBalance(string userId, decimal requestPrice, CancellationToken cancellationToken)
        {
            try
            {
                var user = await _userRepository.GetById(userId, cancellationToken);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return (false, "User not found");
                }

                if (user.UserBalance.Balance < requestPrice)
                {
                    _logger.LogWarning("Insufficient balance for document translation. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                        userId, user.UserBalance.Balance, requestPrice);
                    return (false, "დოკუმენტის თარგმნისთვის არასაკმარისი ბალანსია");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user balance for user {UserId}", userId);
                return (false, "Error checking user balance");
            }
        }

        private static decimal CalculateDocumentTranslationPrice(int pageCount)
        {
            return pageCount * 0.1m;
        }

        private async Task<int> GetDocumentPageCount(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var processor = _fileProcessorFactory.GetProcessor(file.FileName);
                
                // page count for pdf files
                if (file.FileName.ToLowerInvariant().EndsWith(".pdf"))
                {
                    var fileSizeInMB = file.Length / (1024.0 * 1024.0);
                    var estimatedPages = Math.Max(1, (int)Math.Ceiling(fileSizeInMB / 0.5)); // Assume ~0.5MB per page
                    
                    _logger.LogInformation("Estimated page count for PDF {FileName}: {PageCount} pages (based on {FileSize:F2}MB file size)", 
                        file.FileName, estimatedPages, fileSizeInMB);
                    
                    return estimatedPages;
                }
                
                // (DOCX, DOC, TXT), estimate based on file size
                var fileSizeInKB = file.Length / 1024.0;
                var estimatedPagesForDoc = Math.Max(1, (int)Math.Ceiling(fileSizeInKB / 50)); // Assume ~50KB per page for text documents
                
                _logger.LogInformation("Estimated page count for document {FileName}: {PageCount} pages (based on {FileSize:F2}KB file size)", 
                    file.FileName, estimatedPagesForDoc, fileSizeInKB);
                
                return estimatedPagesForDoc;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error estimating page count for file {FileName}, defaulting to 1 page", file.FileName);
                return 1; 
            }
        }

        private async Task LogDocumentTranslationRequest(
            string userId, 
            string fileName, 
            int targetLanguageId, 
            Domain.Models.DocumentFormat outputFormat,
            DocumentTranslationResult result, 
            RequestType requestType, 
            int pageCount, 
            decimal price,
            CancellationToken cancellationToken)
        {
            try
            {
                var requestData = new
                {
                    FileName = fileName,
                    TargetLanguageId = targetLanguageId,
                    OutputFormat = outputFormat.ToString(),
                    PageCount = pageCount,
                    Price = price
                };

                var responseData = new
                {
                    Success = result.Success,
                    TranslationId = result.TranslationId,
                    OutputFormat = result.OutputFormat.ToString(),
                    ContentLength = result.TranslatedContent?.Length ?? 0,
                    ErrorMessage = result.ErrorMessage
                };

                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(requestData, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = requestType
                }, cancellationToken);

                _logger.LogInformation("Document translation request logged. User: {UserId}, RequestType: {RequestType}, PageCount: {PageCount}, Price: {Price}", 
                    userId, requestType, pageCount, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging document translation request for user {UserId}", userId);
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
