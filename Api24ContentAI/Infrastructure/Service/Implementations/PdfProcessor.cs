using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static System.Text.RegularExpressions.Regex;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class PdfProcessor : IPdfProcessor
    {
        private readonly IAIService _aiService;
        private readonly ILanguageService _languageService;
        private readonly IUserService _userService;
        private readonly IGptService _gptService;
        private readonly ILogger<DocumentTranslationService> _logger;

        public PdfProcessor(
            IAIService aiService,
            ILanguageService languageService,
            IUserService userService,
            IGptService gptService,
            ILogger<DocumentTranslationService> logger)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool CanProcess(string fileExtension)
        {
            var ext = fileExtension.ToLowerInvariant();
            return ext is ".pdf";
        }

        public async Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken)
        {
        
            try
            {
                if (file == null || file.Length == 0)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No file provided" };
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };
                }

                var user = await _userService.GetById(userId, cancellationToken);
                if (user == null)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User not found" };
                }

                var ocrTxtContent = await SendFileToOcrService(file, cancellationToken);
                
                var translationResult = await TranslateOcrContent(ocrTxtContent, targetLanguageId, userId, AIModel.Claude4Sonnet, cancellationToken);
                
                if (!translationResult.Success)
                {
                    _logger.LogWarning("Translation failed: {ErrorMessage}", translationResult.ErrorMessage);
                    return translationResult;
                }
                
                if (outputFormat != Domain.Models.DocumentFormat.Markdown)
                {
                    _logger.LogInformation("Note: Requested format was {OutputFormat}, but returning Markdown due to conversion limitations", outputFormat);
                    translationResult.OutputFormat = Domain.Models.DocumentFormat.Markdown;
                    
                    if (!string.IsNullOrEmpty(translationResult.TranslatedContent))
                    {
                        translationResult.FileData = Encoding.UTF8.GetBytes(translationResult.TranslatedContent);
                        translationResult.FileName = $"translated_{translationResult.TranslationId}.md";
                        translationResult.ContentType = "text/markdown";
                    }
                    else
                    {
                        _logger.LogWarning("Translation result has null or empty content");
                        return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation resulted in empty content" };
                    }
                }

                return translationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TranslateDocument");
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error in document translation workflow: {ex.Message}" };
            }
        }
    
        public async Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, AIModel model, CancellationToken cancellationToken)
        {
            if (file?.Length == 0)
                throw new ArgumentException("Uploaded file is empty or null", nameof(file));

            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);

            if (targetLanguage == null || string.IsNullOrWhiteSpace(targetLanguage.Name))
                throw new ArgumentException("Invalid target language ID", nameof(targetLanguageId));

            var screenshotResult = await GetDocumentScreenshots(file, cancellationToken);
            var translatedPages = new List<List<string>>();

            _logger.LogInformation("Processing {PageCount} pages from screenshot service", screenshotResult.Pages.Count);

            foreach (var page in screenshotResult.Pages)
            {
                _logger.LogInformation("Processing page {PageNumber} with {SectionCount} sections (Page {CurrentPage} of {TotalPages})", 
                    page.Page, page.ScreenShots.Count, page.Page, screenshotResult.Pages.Count);
                var pageTranslations = new List<string>();
                var pageHasAnyContent = false;
                
                for (int i = 0; i < page.ScreenShots.Count; i++)
                {
                    var base64Data = page.ScreenShots[i];
                    var sectionName = i switch
                    {
                        0 => "top",
                        1 => "middle", 
                        2 => "bottom",
                        _ => $"section-{i + 1}"
                    };

                    _logger.LogInformation("Processing page {PageNumber} section {SectionIndex} ({SectionName}), has data: {HasData}", 
                        page.Page, i, sectionName, !string.IsNullOrWhiteSpace(base64Data));

                    if (string.IsNullOrWhiteSpace(base64Data))
                    {
                        _logger.LogWarning("Empty screenshot data for page {Page} section {Section}", page.Page, i);
                        pageTranslations.Add(string.Empty);
                        continue;
                    }

                    // Attempt translation with retry logic for failed sections
                    var translatedSection = await TranslateSectionWithRetry(base64Data, page.Page, sectionName, targetLanguage.Name, model, cancellationToken);
                    pageTranslations.Add(translatedSection);
                    
                    if (!string.IsNullOrWhiteSpace(translatedSection))
                    {
                        pageHasAnyContent = true;
                    }
                    
                    _logger.LogInformation("Page {PageNumber} section {SectionName} translation result: {Length} chars", 
                        page.Page, sectionName, translatedSection?.Length ?? 0);
                }

                // Always add the page, even if some sections failed - preserve partial content
                translatedPages.Add(pageTranslations);
                
                _logger.LogInformation("Page {PageNumber} processing complete: {SectionCount} sections, has content: {HasContent}", 
                    page.Page, pageTranslations.Count, pageHasAnyContent);
            }

            var totalSections = translatedPages.Sum(p => p.Count);
            var translatedSections = translatedPages.Sum(p => p.Count(s => !string.IsNullOrWhiteSpace(s)));
            var pagesWithContent = translatedPages.Count(p => p.Any(s => !string.IsNullOrWhiteSpace(s)));
            
            _logger.LogInformation("Translation summary: {TranslatedSections}/{TotalSections} sections translated across {PagesWithContent}/{TotalPages} pages", 
                translatedSections, totalSections, pagesWithContent, translatedPages.Count);

            // Log detailed page statistics
            for (int i = 0; i < translatedPages.Count; i++)
            {
                var pageSections = translatedPages[i];
                var nonEmptySections = pageSections.Count(s => !string.IsNullOrWhiteSpace(s));
                var totalPageContentLength = pageSections.Sum(s => s?.Length ?? 0);
                _logger.LogInformation("Page {PageNumber}: {NonEmptyCount}/{TotalCount} sections with content, total length: {TotalLength} chars", 
                    i + 1, nonEmptySections, pageSections.Count, totalPageContentLength);
            }

            // Only fail if absolutely no content was extracted from any page
            if (translatedSections == 0)
            {
                _logger.LogError("No content could be extracted and translated from any section of the document");
                
                try
                {
                    _logger.LogInformation("Attempting fallback OCR-based translation as image translation completely failed");
                    var fallbackResult = await TranslateWithTesseract(file, targetLanguageId, userId, outputFormat, cancellationToken);
                    
                    if (fallbackResult.Success && !string.IsNullOrWhiteSpace(fallbackResult.TranslatedContent))
                    {
                        _logger.LogInformation("Fallback OCR translation succeeded, returning OCR result");
                        return fallbackResult;
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "Fallback OCR translation also failed");
                }
                
                return new DocumentTranslationResult 
                { 
                    Success = false, 
                    ErrorMessage = "Document translation failed. The document may contain complex formatting or images that couldn't be processed. Please try with a simpler document or contact support." 
                };
            }

            var dataLossPercentage = (double)(totalSections - translatedSections) / totalSections * 100;
            if (dataLossPercentage > 25)
            {
                _logger.LogWarning("Significant data loss detected: {DataLossPercentage:F1}% of sections failed translation", dataLossPercentage);
            }

            var finalMarkdown = await CombineTranslatedSections(translatedPages, targetLanguage.Name, model, cancellationToken);
            
            string improvedTranslation = finalMarkdown;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(finalMarkdown))
            {
                _logger.LogInformation("Starting translation verification for Claude-translated document");
        
                try
                {
                    List<KeyValuePair<int, string>> translationChunksForVerification;
            
                    if (finalMarkdown.Length > 8000)
                    {
                        _logger.LogInformation("Translation is large ({Length} chars), splitting into chunks for verification", finalMarkdown.Length);
                        var chunks = GetChunksOfText(finalMarkdown, 8000);
                        translationChunksForVerification = chunks.Select((chunk, index) => 
                            new KeyValuePair<int, string>(index + 1, chunk)).ToList();
                        _logger.LogInformation("Split into {Count} chunks for verification", chunks.Count);
                    }
                    else
                    {
                        translationChunksForVerification = [new KeyValuePair<int, string>(1, finalMarkdown)];
                    }

                    var verificationResult = new VerificationResult();
                    var verifiedTranslation = "";
                    var response = await ProcessTranslationVerification(
                        cancellationToken, translationChunksForVerification, targetLanguage, finalMarkdown, model);

                    verificationResult = response.verificationResult;
                    verifiedTranslation = response.translatedText;
            
                    if (verificationResult.Success)
                    {
                        improvedTranslation = verifiedTranslation;
                        qualityScore = verificationResult.QualityScore ?? 1.0;
                        _logger.LogInformation("Claude translation verification completed with score: {Score}", qualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("Translation verification failed: {Error}", verificationResult.ErrorMessage);
                        qualityScore = 0.5;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during Claude translation verification, using original translation");
                    qualityScore = 0.5; 
                }
            }

            string translationId = Guid.NewGuid().ToString();
            
            var translationResult = new DocumentTranslationResult
            {
                TranslatedContent = improvedTranslation,
                OutputFormat = outputFormat,
                Success = true,
                TranslationQualityScore = qualityScore,
                TranslationId = translationId
            };
            
            if (outputFormat != Domain.Models.DocumentFormat.Markdown)
            {
                _logger.LogInformation("Note: Requested format was {OutputFormat}, but returning Markdown due to conversion limitations", outputFormat);
                translationResult.OutputFormat = Domain.Models.DocumentFormat.Markdown;
                    
                if (!string.IsNullOrEmpty(translationResult.TranslatedContent))
                {
                    translationResult.FileData = Encoding.UTF8.GetBytes(translationResult.TranslatedContent);
                    translationResult.FileName = $"translated_{translationResult.TranslationId}.md";
                    translationResult.ContentType = "text/markdown";
                }
                else
                {
                    _logger.LogWarning("Translation result has null or empty content");
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation resulted in empty content" };
                }
            }

            return translationResult;
        }

        private async Task<string> TranslateSectionWithRetry(string base64Data, int pageNumber, string sectionName, string targetLanguageName, AIModel model, CancellationToken cancellationToken, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var imageData = Convert.FromBase64String(base64Data);
                    var translated = await ExtractAndTranslateWithClaude(imageData, pageNumber, sectionName, targetLanguageName, model, cancellationToken);
                    
                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        if (attempt > 1)
                        {
                            _logger.LogInformation("Successfully translated page {PageNumber} section {SectionName} on attempt {Attempt}", 
                                pageNumber, sectionName, attempt);
                        }
                        return translated;
                    }
                    else
                    {
                        _logger.LogWarning("Empty translation result for page {PageNumber} section {SectionName} on attempt {Attempt}", 
                            pageNumber, sectionName, attempt);
                    }
                }
                catch (Exception ex) when (IsTimeoutException(ex))
                {
                    lastException = ex;
                    _logger.LogWarning("Timeout on translation attempt {Attempt}/{MaxRetries} for page {PageNumber} section {SectionName}: {ErrorMessage}", 
                        attempt, maxRetries, pageNumber, sectionName, ex.Message);
                    
                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt + 1) * 2000); // 4s, 8s, 16s
                        _logger.LogInformation("Waiting {DelaySeconds} seconds before retry due to timeout", delay.TotalSeconds);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Translation attempt {Attempt}/{MaxRetries} failed for page {PageNumber} section {SectionName}", 
                        attempt, maxRetries, pageNumber, sectionName);
                    
                    if (IsPermanentFailure(ex))
                    {
                        _logger.LogError("Permanent failure detected for page {PageNumber} section {SectionName}, stopping retries: {ErrorMessage}", 
                            pageNumber, sectionName, ex.Message);
                        break;
                    }
                    
                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 1000); // 2s, 4s, 8s
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            
            _logger.LogError(lastException, "All {MaxRetries} translation attempts failed for page {PageNumber} section {SectionName}", 
                maxRetries, pageNumber, sectionName);
            
            return string.Empty;
        }

        private static bool IsTimeoutException(Exception ex)
        {
            return ex is OperationCanceledException ||
                   ex is TaskCanceledException ||
                   (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) ||
                   (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)) ||
                   (ex.InnerException != null && IsTimeoutException(ex.InnerException));
        }

        private static bool IsPermanentFailure(Exception ex)
        {
            return ex.Message.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("400", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Bad Request", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Image data is empty", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Image media type is not specified", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> SendFileToOcrService(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Sending file {FileName} to OCR service, size: {Size} bytes", 
                    file.FileName, file.Length);
                
                using var httpClient = new HttpClient();
                using var formContent = new MultipartFormDataContent();

                await using var fileStream = file.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);
                
                formContent.Add(streamContent, "file", file.FileName);
                
                var response = await httpClient.PostAsync("http://127.0.0.1:8000/ocr", formContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Received OCR response, length: {Length} characters", responseContent.Length);
                
                if (!string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogDebug("OCR response sample: {Sample}", 
                        responseContent[..Math.Min(responseContent.Length, 200)]);
                }
                else
                {
                    _logger.LogWarning("OCR service returned empty response");
                }
                
                return responseContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending file to OCR service");
                throw new Exception($"Failed to process file with OCR service: {ex.Message}");
            }
        }
    
        private async Task<DocumentTranslationResult> TranslateOcrContent(string ocrTxtContent, int targetLanguageId, string userId, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ocrTxtContent))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No OCR content provided" };
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };
                }

                _logger.LogInformation("Deserializing OCR JSON response");
                
                string extractedText = ocrTxtContent;
                
                LanguageModel language;
                try
                {
                    language = await _languageService.GetById(targetLanguageId, cancellationToken);
                    if (language == null)
                    {
                        _logger.LogWarning("Target language not found: {LanguageId}", targetLanguageId);
                        return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language not found" };
                    }
                    _logger.LogInformation("Target language: {LanguageName} (ID: {LanguageId})", language.Name, targetLanguageId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting language with ID {LanguageId}", targetLanguageId);
                    return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error getting language: {ex.Message}" };
                }
                
                List<string> chunks;
                if (extractedText.Length > 8000)
                {
                    _logger.LogInformation("Text is too large ({Length} chars), splitting into chunks", extractedText.Length);
                    chunks = GetChunksOfText(extractedText, 8000);
                    _logger.LogInformation("Split into {Count} chunks", chunks.Count);
                }
                else
                {
                    chunks = [extractedText];
                }

                var translatedChunks = new List<string>();
                var translationChunksForVerification = new List<KeyValuePair<int, string>>();
                
                for (var i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    _logger.LogInformation("Translating chunk {Index}/{Total}, size: {Size} characters", 
                        i + 1, chunks.Count, chunk.Length);
                    
                    var prompt = GenerateTranslationPrompt(language, i, chunks, chunk);
                    try
                    {
                        var message = new ContentFile { Type = "text", Text = prompt };
                        var contentFiles = new List<ContentFile> { message };
                        
                        _logger.LogInformation("Sending chunk {Index}/{Total} to {Model} for translation", 
                            i + 1, chunks.Count, model);
                        
                        var aiResponse = await _aiService.SendRequestWithFile(contentFiles, model, cancellationToken);
                        
                        if (!aiResponse.Success)
                        {
                            _logger.LogWarning("AI service failed for chunk {Index}/{Total}: {Error}", 
                                i + 1, chunks.Count, aiResponse.ErrorMessage);
                            continue;
                        }
                        
                        string translatedChunk = aiResponse.Content?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(translatedChunk))
                        {
                            _logger.LogWarning("{Model} returned empty translation result for chunk {Index}/{Total}", 
                                model, i + 1, chunks.Count);
                            continue;
                        }
                        
                        _logger.LogInformation("Received translation for chunk {Index}/{Total}, length: {Length} characters", 
                            i + 1, chunks.Count, translatedChunk.Length);
                        
                        translatedChunks.Add(translatedChunk);
                        translationChunksForVerification.Add(new KeyValuePair<int, string>(i + 1, translatedChunk));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error translating chunk {Index}/{Total}", i + 1, chunks.Count);
                    }
                }
                
                if (translatedChunks.Count == 0)
                {
                    _logger.LogWarning("All translation chunks failed");
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service failed to translate any content" };
                }
                
                string translatedText = string.Join("\n\n", translatedChunks);
                
                _logger.LogInformation("Combined translated text, total length: {Length} characters", translatedText.Length);

                _logger.LogInformation("Translated {ChunkCount} chunks with lengths: {ChunkLengths}", 
                    translatedChunks.Count,
                    string.Join(", ", translatedChunks.Select(c => c.Length)));
                
                _logger.LogInformation("Starting translation verification for all {Count} chunks...", translationChunksForVerification.Count);
                var verificationResultAndText = await ProcessTranslationVerification(cancellationToken, translationChunksForVerification, language, translatedText, model);
                var verificationResult = verificationResultAndText.Item1;
                translatedText = verificationResultAndText.Item2;

                string translationId = Guid.NewGuid().ToString();
                
                return new DocumentTranslationResult
                {
                    Success = true,
                    OriginalContent = extractedText,
                    TranslatedContent = translatedText,
                    OutputFormat = Domain.Models.DocumentFormat.Markdown,
                    FileName = $"translated_{translationId}.md",
                    ContentType = "text/markdown",
                    TranslationQualityScore = verificationResult.QualityScore ?? (verificationResult.Success ? 1.0 : 0.0),
                    TranslationId = translationId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TranslateOcrJson");
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating OCR content: {ex.Message}" };
            }
        }

        private static List<string> GetChunksOfText(string text, int maxChunkSize)
        {
            var chunks = new List<string>();
            
            var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.None);
            
            var currentChunk = new StringBuilder();
            
            foreach (string paragraph in paragraphs)
            {
                if (currentChunk.Length + paragraph.Length > maxChunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                
                if (paragraph.Length > maxChunkSize)
                {
                    string[] sentences = Split(paragraph, @"(?<=[.!?])\s+");
                    
                    foreach (string sentence in sentences)
                    {
                        if (currentChunk.Length + sentence.Length > maxChunkSize && currentChunk.Length > 0)
                        {
                            chunks.Add(currentChunk.ToString().Trim());
                            currentChunk.Clear();
                        }
                        
                        currentChunk.Append(sentence + " ");
                    }
                }
                else
                {
                    currentChunk.AppendLine(paragraph);
                    currentChunk.AppendLine();
                }
            }
            
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }
            
            return chunks;
        }
    
        private async Task<(VerificationResult verificationResult, string translatedText)> ProcessTranslationVerification(CancellationToken cancellationToken,
            List<KeyValuePair<int, string>> translationChunksForVerification, LanguageModel language, string translatedText, AIModel model)
        {
            VerificationResult verificationResult;
            try
            {
                verificationResult = await _gptService.VerifyTranslationBatch(
                    translationChunksForVerification, cancellationToken);
                    
                _logger.LogInformation("Translation verification completed. Success: {Success}, Score: {Score}, Verified: {Verified}/{Total}", 
                    verificationResult.Success, 
                    verificationResult.QualityScore,
                    verificationResult.VerifiedChunks,
                    translationChunksForVerification.Count);
                    
                if (verificationResult.ChunkWarnings is { Count: > 0 })
                {
                    foreach (var warning in verificationResult.ChunkWarnings)
                    {
                        _logger.LogWarning("Chunk {ChunkId} verification warning: {Warning}", 
                            warning.Key, warning.Value);
                    }
                }
                    
                if (verificationResult.Success && 
                    verificationResult.QualityScore < 0.8 && 
                    !string.IsNullOrEmpty(verificationResult.Feedback))
                {
                    _logger.LogInformation("GPT suggested improvements: {Feedback}", verificationResult.Feedback);
                        
                    string improvedTranslation = await ImproveTranslationWithFeedback(
                        translatedText, language.Name, verificationResult.Feedback, model, cancellationToken);
                        
                    if (!string.IsNullOrEmpty(improvedTranslation))
                    {
                        _logger.LogInformation("Applied GPT's suggestions to improve translation");
                        translatedText = improvedTranslation;
                            
                        _logger.LogInformation("Re-verifying improved translation...");
                        
                        var improvedVerification = await _gptService.EvaluateTranslationQuality(
                            $"Evaluate this translation to {language.Name}:\n\n{improvedTranslation}", 
                            cancellationToken);
                            
                        if (improvedVerification.Success)
                        {
                            _logger.LogInformation("Improved translation verification score: {Score} (was: {OldScore})", improvedVerification.QualityScore, verificationResult.QualityScore);
                            verificationResult = improvedVerification;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying translation");
                verificationResult = new VerificationResult { Success = false, ErrorMessage = ex.Message };
            }

            return (verificationResult, translatedText);
        }
    
        private async Task<string> ImproveTranslationWithFeedback(string translatedText, string targetLanguage, string feedback, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Attempting to improve translation based on GPT feedback");
                
                var prompt = GenerateImprovedTranslationPrompt(translatedText, targetLanguage, feedback);
                
                var contentFiles = new List<ContentFile> { new ContentFile { Type = "text", Text = prompt } };
                
                _logger.LogInformation("Sending improvement request to {Model}", model);
                var aiResponse = await _aiService.SendRequestWithFile(contentFiles, model, cancellationToken);
                
                if (!aiResponse.Success)
                {
                    _logger.LogWarning("AI service failed for translation improvement: {Error}", aiResponse.ErrorMessage);
                    return string.Empty;
                }
                
                string improvedTranslation = aiResponse.Content?.Trim() ?? string.Empty;
                
                if (improvedTranslation.Contains("Here's the improved translation") || 
                    improvedTranslation.Contains("Improved translation:"))
                {
                    int startIndex = improvedTranslation.IndexOf('\n');
                    if (startIndex > 0)
                    {
                        improvedTranslation = improvedTranslation[(startIndex + 1)..].Trim();
                    }
                }
                
                if (string.IsNullOrWhiteSpace(improvedTranslation))
                {
                    _logger.LogWarning("Claude returned empty improvement result");
                    return string.Empty;
                }
                
                _logger.LogInformation("Received improved translation, length: {Length} characters", 
                    improvedTranslation.Length);

                if (!(Math.Abs(improvedTranslation.Length - translatedText.Length) < translatedText.Length * 0.05))
                    return improvedTranslation;
                
                _logger.LogInformation("Improved translation has similar length to original, performing quality check");
                    
                var verificationPrompt = GenerateVerificationPrompt(translatedText, targetLanguage, improvedTranslation);
                    
                var verificationResult = await _gptService.EvaluateTranslationQuality(verificationPrompt, cancellationToken);
                    
                if (verificationResult.Success && verificationResult.Feedback.StartsWith("A|"))
                {
                    _logger.LogInformation("Verification indicates original translation was better, keeping original");
                    return string.Empty;
                }

                return improvedTranslation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error improving translation with feedback");
                return string.Empty;
            }
        }
    
        private async Task<ScreenShotResult> GetDocumentScreenshots(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Uploaded file is empty or null", nameof(file));

            using var httpClient = new HttpClient();
            await using var fileStream = file.OpenReadStream();
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", file.FileName);
            
            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync("http://127.0.0.1:8000/screenshot", content, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("Failed to contact screenshot service", ex);
            }

            var result = await response.Content.ReadFromJsonAsync<ScreenShotResult>(cancellationToken: cancellationToken);
            if (result == null)
                throw new InvalidOperationException("Screenshot service returned null");

            return result;
        }
    
        private async Task<string> ExtractAndTranslateWithClaude(byte[] imageData, int pageNumber, string sectionName, string targetLanguageName, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Extracting and translating text from page {PageNumber} {SectionName} section with {Model}", pageNumber, sectionName, model);
        
                string base64Image = Convert.ToBase64String(imageData);
        
                var messages = new List<ContentFile>
                {
                    new ContentFile 
                    { 
                        Type = "text", 
                        Text = model == AIModel.Gemini25Pro 
                            ? ExtractTextAndTranslateForGemini(pageNumber, sectionName, targetLanguageName)
                            : ExtractTextAndTranslate(pageNumber, sectionName, targetLanguageName)
                    },
                    new ContentFile 
                    { 
                        Type = "image", 
                        Source = new Source()
                        {
                            Type = "base64",
                            MediaType = "image/png",
                            Data = base64Image
                        }
                    }
                };
        
                var aiResponse = await _aiService.SendRequestWithFile(messages, model, cancellationToken);
        
                var content = aiResponse.Content;
                if (content == null)
                {
                    _logger.LogWarning("No content received from {Model} for page {PageNumber} {SectionName} section", model, pageNumber, sectionName);
                    return string.Empty;
                }
        
                string translatedText = content.Trim();
        
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    _logger.LogWarning("{Model} returned empty result for page {PageNumber} {SectionName} section", model, pageNumber, sectionName);
                    return string.Empty;
                }
        
                if (translatedText.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                    translatedText.StartsWith("I've translated", StringComparison.OrdinalIgnoreCase) ||
                    translatedText.StartsWith("The translated", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = translatedText.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        translatedText = translatedText.Substring(firstLineBreak + 1).Trim();
                    }
                }
        
                _logger.LogInformation("Successfully extracted and translated {Length} characters from page {PageNumber} {SectionName} section using {Model}", 
                    translatedText.Length, pageNumber, sectionName, model);
        
                return translatedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting and translating text with {Model} from page {PageNumber} {SectionName} section", model, pageNumber, sectionName);
                return string.Empty;
            }
        }

        private static string ExtractTextAndTranslateForGemini(int pageNumber, string sectionName, string targetLanguageName)
        {
            string verticalPortion = sectionName switch
            {
                "top" => "0-50%",
                "middle" => "25-75%", 
                "bottom" => "50-100%",
                _ => "full page"
            };

            return $@"
                You are a professional document translator. Please analyze this image section from page {pageNumber} ({sectionName} section, approximately {verticalPortion} of the page) and:

                1. Extract all visible text accurately
                2. Translate everything to {targetLanguageName}
                3. Format the output as clean HTML using only these tags: <h1>-<h6>, <p>, <ul>, <ol>, <li>, <table>, <tr>, <th>, <td>, <strong>, <em>, <br>, <hr>, <pre>, <code>

                Important rules:
                    - Translate ALL text content including headers, labels, contact information, and descriptions
                    - Keep technical codes, standards (ISO, EN, etc.), and reference numbers unchanged
                    - Preserve the document structure using appropriate HTML tags
                    - Use <br> tags for line breaks, not \\n
                    - Do not add explanatory text or comments
                    - Output only the translated HTML content

                Translate to: {targetLanguageName}
            ";
        }

        private static string ExtractTextAndTranslate(int pageNumber, string sectionName, string targetLanguageName)
        {
            string verticalPortion = sectionName switch
            {
                "top" => "0-50%",
                "middle" => "25-75%",
                "bottom" => "50-100%",
                _ => "full page"
            };

            return $@"
                <role>
                    You are an advanced OCR and translation system.
                    Your task is to process an image snippet from a document.
                </role>

                <context>
                    Document Context:
                        - Page Number: {pageNumber}
                        - Section of Page: {sectionName} (approx. {verticalPortion} vertical portion)
                </context>

                <actions>
                    1. <Extract Text>
                        Accurately extract **all** visible textual content from the provided image snippet.

                    2. <Translate>
                        Translate the entire extracted text into **{targetLanguageName}**.

                    3. <Format as HTML>
                        Present the translated content using **strict HTML tags only**. Markdown is prohibited.
                        Use these HTML elements appropriately:
                        - Headings: `<h1>`, `<h2>`, `<h3>`, etc.
                        - Lists: `<ul>`, `<ol>`, with `<li>`
                        - Emphasis: `<strong>` for bold, `<em>` for italic
                        - Tables: `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`
                        - Separators: `<hr />` for distinct section breaks
                        - Code/Technical blocks: `<pre>`, `<code>`

                    4. <Preserve Original Data>
                        Keep all numbers, dates, and codes (except technical standards) exactly as in the source or transliterate them suitably for {targetLanguageName}.

                    5. <Non-Translation Rules>
                        **Do not translate** the following; preserve exactly as in source:
                        - Technical identifiers
                        - Standards (e.g., ISO, EN, ДСТУ, ГОСТ)
                        - Specific codes (e.g., ДФРПОУ, НААУ)
                        - Reference numbers or part numbers

                    6. <Proper Nouns>
                        Transliterate proper nouns per standard {targetLanguageName} rules unless a widely accepted translation exists.

                    7. <Contextual Structure>
                        Use context to infer and apply correct document structure in HTML: titles, sections, lists, paragraphs, etc.
                </actions>

                <output_requirements>
                    - Output **only** the translated text in {targetLanguageName}
                    - Format the entire output strictly in HTML as described
                    - Do **not** include any explanations, remarks, or original untranslated text
                    - Do **not** add any introductory or concluding statements
                </output_requirements>

                ";
        }
    
        private async Task<string> CombineTranslatedSections(List<List<string>> translatedPageSections, string targetLanguageName, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Combining translated sections from {PageCount} pages using comprehensive multi-stage approach", translatedPageSections.Count);

                var translatedPages = new List<string>();
                var partiallyFailedPages = new List<int>();

                for (int i = 0; i < translatedPageSections.Count; i++)
                {
                    var pageSections = translatedPageSections[i];
                    var validSections = pageSections?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                    var totalSections = pageSections?.Count ?? 0;
                    
                    _logger.LogInformation("Processing page {PageNumber}: {ValidSections}/{TotalSections} sections have content", 
                        i + 1, validSections.Count, totalSections);

                    if (validSections.Count == 0)
                    {
                        _logger.LogWarning("Page {PageNumber} has no valid sections - adding placeholder", i + 1);
                        translatedPages.Add($"<!-- Page {i + 1}: No content could be extracted -->");
                        partiallyFailedPages.Add(i + 1);
                        continue;
                    }

                    if (validSections.Count < totalSections)
                    {
                        _logger.LogWarning("Page {PageNumber} is missing {MissingSections}/{TotalSections} sections", 
                            i + 1, totalSections - validSections.Count, totalSections);
                        partiallyFailedPages.Add(i + 1);
                    }

                    string combinedPage;
                    if (validSections.Count == 1)
                    {
                        combinedPage = validSections[0];
                        _logger.LogInformation("Page {PageNumber} has single valid section, using directly", i + 1);
                    }
                    else
                    {
                        combinedPage = await CombineSectionsForSinglePageWithFallback(validSections, targetLanguageName, model, i + 1, cancellationToken);
                    }
                    
                    translatedPages.Add(combinedPage);
                    _logger.LogInformation("Page {PageNumber} combined successfully, final length: {Length} chars", i + 1, combinedPage.Length);
                }

                _logger.LogInformation("Stage 1 complete: Combined {ProcessedPages} pages, {PartiallyFailedCount} had missing sections", 
                    translatedPages.Count, partiallyFailedPages.Count);

                if (partiallyFailedPages.Count > 0)
                {
                    _logger.LogWarning("Pages with missing sections: {PartiallyFailedPages}", string.Join(", ", partiallyFailedPages));
                }

                var validPages = translatedPages.Where(p => !p.StartsWith("<!-- Page") || !p.Contains("No content could be extracted")).ToList();
                var emptyPageCount = translatedPages.Count - validPages.Count;
                
                if (emptyPageCount > 0)
                {
                    _logger.LogWarning("Removed {EmptyPageCount} completely empty pages from final document", emptyPageCount);
                }

                if (validPages.Count == 0)
                {
                    _logger.LogError("All pages resulted in empty content after combination.");
                    return string.Empty;
                }

                if (validPages.Count == 1)
                {
                    _logger.LogInformation("Single valid page document, returning combined page directly");
                    return validPages[0];
                }

                _logger.LogInformation("Performing document-level assembly for {PageCount} valid pages using AI");
                
                var documentAssembly = new StringBuilder();
                for (int i = 0; i < validPages.Count; i++)
                {
                    documentAssembly.AppendLine($"PAGE {i + 1}:");
                    documentAssembly.AppendLine(validPages[i]);
                    documentAssembly.AppendLine();
                    
                    _logger.LogInformation("Added page {PageNumber} to document assembly, page length: {Length} chars", 
                        i + 1, validPages[i].Length);
                }

                _logger.LogInformation("Document assembly created with {TotalLength} total characters for {PageCount} pages", 
                    documentAssembly.Length, validPages.Count);

                var finalDocument = await CombineDocumentPagesWithClaudeAndFallback(documentAssembly.ToString(), targetLanguageName, model, validPages, cancellationToken);
                
                _logger.LogInformation("Successfully assembled final document of length {Length}", finalDocument.Length);
                return finalDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error combining translated sections with Claude, using emergency fallback");
                return CreateEmergencyFallbackCombination(translatedPageSections);
            }
        }

        private async Task<string> CombineSectionsForSinglePageWithFallback(List<string> validSections, string targetLanguageName, AIModel model, int pageNumber, CancellationToken cancellationToken)
        {
            try
            {
                var sectionsContent = new StringBuilder();
                for (int i = 0; i < validSections.Count; i++)
                {
                    sectionsContent.AppendLine($"SECTION {i + 1}:\n{validSections[i]}\n");
                }

                var prompt = model == AIModel.Gemini25Pro 
                    ? GenerateSinglePageCombinationPromptForGemini(targetLanguageName, sectionsContent.ToString())
                    : GenerateSinglePageCombinationPrompt(targetLanguageName, sectionsContent.ToString());

                var message = new ContentFile { Type = "text", Text = prompt };
                var response = await _aiService.SendRequestWithFile([message], model, cancellationToken);

                if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.LogWarning("AI combination failed for page {PageNumber} using {Model}, using simple concatenation fallback", pageNumber, model);
                    return string.Join("\n\n", validSections);
                }

                var combinedPage = response.Content.Trim();
                
                if (combinedPage.StartsWith("Here", StringComparison.OrdinalIgnoreCase) ||
                    combinedPage.StartsWith("I've combined", StringComparison.OrdinalIgnoreCase) ||
                    combinedPage.StartsWith("Here's the", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = combinedPage.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        combinedPage = combinedPage.Substring(firstLineBreak + 1).Trim();
                    }
                }
                
                if (string.IsNullOrWhiteSpace(combinedPage))
                {
                    _logger.LogWarning("AI combination returned empty result for page {PageNumber} using {Model}, using concatenation fallback", pageNumber, model);
                    return string.Join("\n\n", validSections);
                }
                
                _logger.LogInformation("Successfully combined sections for page {PageNumber} using {Model}, result length: {Length}", pageNumber, model, combinedPage.Length);
                return combinedPage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error combining sections for page {PageNumber} using {Model}, using concatenation fallback", pageNumber, model);
                return string.Join("\n\n", validSections);
            }
        }

        private static string GenerateSinglePageCombinationPromptForGemini(string targetLanguageName, string sectionsContent)
        {
            return $@"
                Combine these overlapping text sections from a single document page into one seamless text in {targetLanguageName}. The sections have overlapping content - merge them by removing duplicates while preserving all unique information.

                {sectionsContent}

                Rules:
                    - Combine overlapping content smoothly
                    - Keep all unique information
                    - Maintain HTML formatting
                    - Output only the combined text
                    - Do not add explanatory comments

                Combined result:
                ";
        }

        private async Task<string> CombineDocumentPagesWithClaudeAndFallback(string pagesContent, string targetLanguageName, AIModel model, List<string> validPages, CancellationToken cancellationToken)
        {
            try
            {
                var finalDocument = await CombineDocumentPages(pagesContent, targetLanguageName, model, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(finalDocument))
                {
                    _logger.LogWarning("Document-level AI assembly failed using {Model}, using intelligent page concatenation", model);
                    return CreateIntelligentPageConcatenation(validPages);
                }

                return finalDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in document-level assembly using {Model}, using intelligent page concatenation", model);
                return CreateIntelligentPageConcatenation(validPages);
            }
        }

        private async Task<string> CombineDocumentPages(string pagesContent, string targetLanguageName, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Performing document-level assembly with {Model} using sophisticated prompt", model);
                _logger.LogInformation("Input to document assembly: {InputLength} characters", pagesContent.Length);
                
                var lines = pagesContent.Split('\n');
                var pageHeaders = lines.Where(line => line.StartsWith("PAGE ")).ToList();
                _logger.LogInformation("Found {PageHeaderCount} page headers in assembly input: {PageHeaders}", 
                    pageHeaders.Count, string.Join(", ", pageHeaders));
                
                var prompt = model == AIModel.Gemini25Pro 
                    ? GenerateDocumentCombinationPromptForGemini(targetLanguageName, pagesContent)
                    : GenerateDocumentCombinationPrompt(targetLanguageName, pagesContent);
                
                var message = new ContentFile { Type = "text", Text = prompt };
                var response = await _aiService.SendRequestWithFile([message], model, cancellationToken);

                if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.LogWarning("Document-level assembly failed using {Model}. AI service returned an error or empty content. Error: {ErrorMessage}", model, response.ErrorMessage);
                    return string.Empty;
                }

                _logger.LogInformation("{Model} returned response with {ResponseLength} characters", model, response.Content.Length);

                var finalDocument = response.Content.Trim();
                
                if (finalDocument.StartsWith("Here", StringComparison.OrdinalIgnoreCase) ||
                    finalDocument.StartsWith("I've combined", StringComparison.OrdinalIgnoreCase) ||
                    finalDocument.StartsWith("Here's the", StringComparison.OrdinalIgnoreCase) ||
                    finalDocument.StartsWith("The complete", StringComparison.OrdinalIgnoreCase) ||
                    finalDocument.StartsWith("The assembled", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = finalDocument.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        _logger.LogInformation("Removing AI response prefix, original length: {OriginalLength}, new length will be: {NewLength}", 
                            finalDocument.Length, finalDocument.Length - firstLineBreak - 1);
                        finalDocument = finalDocument.Substring(firstLineBreak + 1).Trim();
                    }
                }
                
                _logger.LogInformation("Successfully assembled final document using {Model}, result length: {Length}", model, finalDocument.Length);
                
                if (finalDocument.Length > 200)
                {
                    _logger.LogInformation("Final document begins with: {Beginning}...", finalDocument.Substring(0, 100));
                    _logger.LogInformation("Final document ends with: ...{Ending}", finalDocument.Substring(finalDocument.Length - 100));
                }
                
                return finalDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in document-level assembly with {Model}", model);
                return string.Empty;
            }
        }

        private static string GenerateDocumentCombinationPromptForGemini(string targetLanguageName, string chunkContent)
        {
            return $@"
                Combine these translated document pages into one complete document in {targetLanguageName}. Each page is labeled with PAGE X markers.

                {chunkContent}

                Rules:
                    - Merge all pages in order
                    - Remove PAGE markers from final output
                    - Combine any overlapping content between pages
                    - Maintain HTML formatting
                    - Output only the complete document
                    - Do not add explanatory text

                Complete document:
                ";
        }

        private static string GenerateVerificationPrompt(string translatedText, string targetLanguage, string improvedTranslation)
        {
            return $@"
                <role>
                You are a rigorous and detail-oriented Translation Quality Assurance Specialist.
                Your mission is to critically evaluate two translations into {targetLanguage} and identify the superior version.
                </role>

                <task>
                You are provided with two translations. Analyze them as if the original source text is known to you implicitly.

                TRANSLATION A:
                {translatedText}

                TRANSLATION B:
                {improvedTranslation}
                </task>

                <criteria>
                    Judge the translations using the following standards:

                    1. <Fluency and Naturalness>: Does the translation read smoothly and idiomatically in {targetLanguage}? Would a native speaker find it natural?
                    2. <Terminology Consistency>: Is domain-specific or repeated vocabulary used consistently and appropriately?
                    3. <Completeness>: Are all elements of the presumed source text fully conveyed, with no omissions or unjustified additions?
                    4. <Accuracy and Fidelity>: Which version better preserves the meaning, nuance, and intent of the original text?

                    <response_instructions>
                    Provide your final decision in the format: <A or B>|<one-sentence rationale>
                    Only respond with the better translation and a short justification.

                    Example: B|Translation B maintains consistent terminology and reads more naturally in {targetLanguage}.
                    </response_instructions>
                </criteria>

                ";
        }

                private static string GenerateTranslationPrompt(LanguageModel language, int i, List<string> chunks, string chunk)
        {
            return $@"
                <role>
                You are an expert multilingual document translator and formatter with extensive experience in OCR text processing. You specialize in converting raw OCR-extracted text into professionally polished, accurately translated documents in {language.Name} while maintaining perfect structural fidelity to the original layout and formatting.
                </role>

                <context>
                You are processing a document that has been extracted from a PDF using OCR technology. The text may contain OCR artifacts, formatting irregularities, and structural elements that need to be preserved during translation. This is chunk {i + 1} of {chunks.Count} total chunks from the document.
                </context>

                <primary_task>
                Translate the provided OCR-extracted text chunk into {language.Name} with absolute precision, maintaining the original document's visual structure, hierarchy, and formatting while ensuring professional-quality translation that preserves all semantic meaning and technical accuracy.
                </primary_task>

                <translation_instructions>
                <content_translation>
                - Translate ALL textual content into {language.Name} with no exceptions
                - This includes contact information labels, job titles, signatures, and descriptive text
                - Ensure translations are natural, fluent, and professionally appropriate for the document type
                - Use standard technical terminology and industry-specific vocabulary appropriate for {language.Name}
                - Maintain the same level of formality and tone as the original text
                - Preserve all semantic nuances and contextual meaning
                - Do not omit, skip, or summarize any translatable content
                - Translate all repeated content exactly as it appears - do not deduplicate
                - Handle idiomatic expressions by finding appropriate {language.Name} equivalents
                - Pay special attention to signatures, titles, and contact sections - these MUST be translated
                </content_translation>

                <preservation_rules>
                    <non_translatable_elements>
                        The following elements must remain EXACTLY as they appear in the source text:
                        - Technical identifiers (part numbers, model numbers, serial numbers)
                        - Official standards and certifications (ISO 9001, ДСТУ Б В.2.7-170:2008, EN standards, etc.)
                        - Specific codes and reference numbers (EAN codes, НААУ, ДФРПОУ, etc.)
                        - Mathematical formulas and equations
                        - Chemical formulas and scientific notation
                        - URLs, email addresses, and web links
                        - Timestamps and date formats that follow specific standards
                        - Currency symbols and monetary amounts (preserve original format)
                        - Measurement units and their abbreviations
                        - Brand names and trademarked terms
                        - Legal document reference numbers
                    </non_translatable_elements>

                <contact_information_rules>
                    For contact information and business details:
                    - Company names and brand names: Keep in original language
                    - Email addresses and URLs: Keep exactly as shown
                    - Phone/fax numbers: Keep numbers as shown
                    - Street addresses: Translate descriptive parts (Street, Avenue, Building, etc.) but keep proper nouns
                    - Contact labels: MUST translate labels like ""Phone"", ""Fax"", ""Email"", ""Website"", ""Address"" into {language.Name}
                    - Certification listings: Keep certification names (ISO 9001, CE, etc.) but translate descriptive text
                    - Signatures and titles: MUST translate personal titles and roles into {language.Name}
                </contact_information_rules>

                    <data_integrity>
                        - Preserve all numbers, dates, and numerical data in their original form
                        - Maintain original punctuation for technical data
                        - Keep decimal separators and number formatting as in source
                        - Preserve mathematical and scientific notation exactly
                        - Maintain original capitalization for codes and identifiers
                        - Transliterate proper names according to established {language.Name} conventions
                        - For organization names, use officially accepted translations if they exist, otherwise transliterate
                        - For geographic locations, use standard {language.Name} place names
                    </data_integrity>
                </preservation_rules>

                <ocr_processing>
                    <error_detection>
                        - Identify and correct common OCR errors such as:
                        * Character substitutions (0/O, 1/l/I, rn/m, etc.)
                        * Garbled or corrupted characters
                        * Incorrect spacing or word breaks
                        * Missing or extra punctuation
                        * Misaligned text fragments
                        - When uncertain about OCR corrections, prioritize faithful transcription over guessing
                        - Preserve intentional formatting even if it appears irregular
                        - Maintain original spacing for technical diagrams or formatted data
                    </error_detection>
                </ocr_processing>

                <structural_formatting>
                    <html_requirements>
                        You must format the translated text using ONLY HTML tags. Do NOT use any Markdown formatting under any circumstances.

                        Available HTML elements:
                        - Headings: <h1>, <h2>, <h3>, <h4>, <h5>, <h6>
                        - Paragraphs: <p>
                        - Lists: <ul> (unordered), <ol> (ordered) with <li> items
                        - Tables: <table>, <thead>, <tbody>, <tr>, <th>, <td>
                        - Code/preformatted: <pre>, <code>
                        - Line breaks: <br />
                        - Horizontal rules: <hr />
                        - Text formatting: <strong>, <em>, <u> (use sparingly, only when clearly indicated)
                        - Divisions: <div> (for complex layouts when necessary)
                    </html_requirements>

                    <header_application_rules>
                        Apply header tags (<h1>, <h2>, <h3>, etc.) ONLY when the source text explicitly demonstrates hierarchical structure through:
                        - Clear section titles or chapter headings
                        - Numbered sections (1., 2., 3. or I., II., III.)
                        - Visually distinct headings in the original layout
                        - Text that serves as a title or subtitle for following content

                        DO NOT apply header tags to:
                        - Regular paragraphs or sentences
                        - Text that is simply bold or emphasized
                        - List items or bullet points
                        - Table headers (use <th> instead)
                        - Captions or labels

                        When in doubt, use <p> tags instead of headers.
                    </header_application_rules>

                    <layout_preservation>
                        - Reproduce the exact visual hierarchy and structure from the source
                        - Maintain paragraph breaks exactly as they appear
                        - Preserve meaningful line breaks within content using <br />
                        - Maintain the sequence and order of all sections
                        - Keep table structures intact with proper HTML table formatting
                        - Preserve list formatting and indentation levels
                        - Maintain spacing between sections using appropriate HTML elements
                        - Ensure consistent formatting throughout the document
                    </layout_preservation>

                    <table_formatting>
                        When encountering tabular data:
                        - Use proper HTML table structure with <table>, <thead>, <tbody>
                        - Apply <th> tags for header cells
                        - Use <td> for data cells
                        - Preserve column alignment and structure
                        - Maintain row and column spans if present
                        - Translate table content while preserving table structure
                    </table_formatting>
                </structural_formatting>

                <quality_assurance>
                    - Ensure all HTML tags are properly nested and closed
                    - Validate that the output is clean, valid HTML
                    - Check that no content has been accidentally omitted
                    - Verify that the translation maintains professional quality
                    - Confirm that technical terms are accurately translated
                    - Ensure consistent formatting throughout the output
                </quality_assurance>

                <output_constraints>
                    - Your response must contain ONLY the final translated HTML content
                    - Do NOT include any explanations, comments, or meta-commentary
                    - Do NOT include the original text or show translation comparisons
                    - Do NOT add any English text or annotations
                    - Do NOT use any Markdown formatting
                    - Begin your response immediately with the translated HTML content
                    - End your response immediately after the last HTML tag
                </output_constraints>

                <source_text>
                    {chunk}
                </source_text>

                Translate the above text following all instructions precisely:";
        }
    
        private static string GenerateImprovedTranslationPrompt(string translatedText, string targetLanguage, string feedback)
        {
            return $@"
                    <role>
                        You are an expert {targetLanguage} translator and editor.
                        Your task is to refine the given translation by addressing quality review feedback thoroughly.
                    </role>

                    <feedback>
                        Quality Review Feedback:
                        {feedback}
                    </feedback>

                    <objective>
                        Improve the provided translation by focusing on:

                        1. <Untranslated Content> Identify and translate any untranslated words or phrases.
                        2. <Terminology Cohesion> Ensure consistent and uniform terminology throughout.
                        3. <Natural Phrasing> Fix awkward or unnatural expressions to flow smoothly and idiomatically in {targetLanguage}.
                        4. <Formatting Integrity> Preserve and correct any formatting or structural inconsistencies.
                        5. <Technical Accuracy> Verify and correct technical term translations using standard {targetLanguage} equivalents.
                        6. <Preservation of Identifiers> CRITICAL: Preserve all technical identifiers, standards (e.g., ISO, EN), codes, and reference numbers exactly as they appear; do NOT translate these.
                    </objective>

                    <instructions>
                        - Output ONLY the fully improved translation.
                        - DO NOT include intros, explanations, apologies, or change summaries.
                        - Format all output using strict HTML tags ONLY. No Markdown or other markup allowed.
                        - Use HTML elements to represent structure (e.g., `<p>`, `<h1>`, `<ul>`, `<li>`, `<table>`, `<pre>`, `<br>`, `<hr>`).
                        - Ensure clean, valid HTML with proper nesting and no inline styles.
                    </instructions>

                    <current_translation>
                    {translatedText}
                    </current_translation>

                ";
        }
    
        private static string GenerateDocumentCombinationPrompt(string targetLanguageName, string chunkContent)
        {
            return
                $@"
                <role>
                    You are an intelligent document reconstruction expert, proficient in {targetLanguageName} and HTML formatting.
                </role>

                <context>
                    You have multiple extracted and translated sections from a document. The sections were generated from overlapping screenshots of the document pages.
                    Each section is labeled with its original page and location (e.g., ""PAGE 1 TOP"", ""PAGE 1 MIDDLE"", ""PAGE 2 TOP"").
                </context>

                <mission>
                    Your mission is to synthesize these sections into one perfectly ordered, de-duplicated, and coherent document.
                </mission>

                <objectives>
                    1. <Combine and Order>
                    Merge all sections into a single cohesive document in {targetLanguageName}, using the page and section markers strictly to determine correct order.

                    2. <Seamless Stitching from Overlap>
                    Use the overlapping content between sections (e.g., between TOP and MIDDLE) to stitch them together seamlessly. It is critical that **no content from the beginning of the first section or the end of the last section of a page is lost**. Your goal is to reconstruct the full, original text from these overlapping pieces.

                    3. <Preserve Structure>
                    Maintain the document's inherent structure (headings, paragraphs, lists, tables, etc.) as implied by content and existing HTML tags.

                    4. <Ensure Natural Flow>
                    The final document should read smoothly and logically from start to finish.

                    5. <Maintain HTML Formatting>
                    Preserve and consistently apply all HTML formatting present in the chunks (headings <h1>–<h6>, lists <ul>, <ol>, tables <table>, emphasis <strong>, <em>, etc.).

                    6. <Content Fidelity>
                    Do NOT add any new content or information beyond what exists in the provided sections.

                    7. <Clean Output>
                        The page and section markers (e.g., `PAGE 1 - SECTION 1 (TOP)`) are for your guidance only and must be removed from the final output.
                </objectives>

                <output_requirements>
                    - Return ONLY the final, seamlessly combined document.
                    - The output must be in {targetLanguageName}, formatted strictly in clean, valid HTML.
                    - Absolutely NO explanations, notes, or comments.
                </output_requirements>

                <document_sections>
                    Here are the document sections to combine:

                    {chunkContent}
                </document_sections>

            ";
        }


        private static string GenerateSinglePageCombinationPrompt(string targetLanguageName, string sectionsContent)
        {
            return $@"
                <role>
                    You are an intelligent document reconstruction expert, proficient in {targetLanguageName} and HTML formatting.
                </role>

                <context>
                    You have been given several translated text sections that were extracted from different overlapping parts of a SINGLE document page. The sections are provided in their order of appearance on the page.
                </context>

                <mission>
                    Your mission is to synthesize these sections into one perfectly ordered and coherent page of text.
                </mission>

                <objectives>
                    1. <Combine and Order>
                       Merge all provided sections into a single cohesive text block.

                    2. <Seamless Stitching from Overlap>
                       The sections have overlapping content. Use this overlap to perfectly stitch them together into a single, continuous text. It is critical that **no content from the beginning of the first section or the end of the last section is lost**. Your goal is to reconstruct the full, original text of the page from these overlapping pieces.

                    3. <Preserve Structure and Formatting>
                       Maintain the original structure (headings, paragraphs, lists, tables) and all HTML formatting present in the sections.

                    4. <Content Fidelity>
                       Do NOT add any new content or information. The output should only be the combined text.
                </objectives>

                <output_requirements>
                    - Return ONLY the final, seamlessly combined text.
                    - The output must be in {targetLanguageName}, formatted strictly in clean, valid HTML.
                    - Absolutely NO explanations, notes, or comments.
                </output_requirements>

                <document_sections>
                    Here are the document sections to combine for this single page:

                    {sectionsContent}
                </document_sections>
            ";
        }

        private string CreateIntelligentPageConcatenation(List<string> validPages)
        {
            try
            {
                var result = new StringBuilder();
                
                for (int i = 0; i < validPages.Count; i++)
                {
                    if (i > 0)
                    {
                        result.AppendLine();
                        result.AppendLine($"<hr class=\"page-break\" data-page=\"{i + 1}\" />");
                        result.AppendLine();
                    }
                    
                    result.AppendLine(validPages[i]);
                }

                var finalResult = result.ToString();
                _logger.LogInformation("Intelligent page concatenation complete, final length: {Length} chars", finalResult.Length);
                return finalResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in intelligent page concatenation, using simple concatenation");
                return string.Join("\n\n<hr class=\"page-break\" />\n\n", validPages);
            }
        }

        private string CreateEmergencyFallbackCombination(List<List<string>> translatedPageSections)
        {
            try
            {
                _logger.LogWarning("Using emergency fallback combination - simply concatenating all non-empty sections");
                
                var allContent = new StringBuilder();
                int pageNum = 1;
                
                foreach (var pageSections in translatedPageSections)
                {
                    var validSections = pageSections?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                    
                    if (validSections.Any())
                    {
                        if (allContent.Length > 0)
                        {
                            allContent.AppendLine();
                            allContent.AppendLine($"<!-- PAGE {pageNum} -->");
                            allContent.AppendLine();
                        }
                        
                        foreach (var section in validSections)
                        {
                            allContent.AppendLine(section);
                            allContent.AppendLine();
                        }
                    }
                    
                    pageNum++;
                }

                var result = allContent.ToString();
                _logger.LogInformation("Emergency fallback combination complete, length: {Length} chars", result.Length);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Emergency fallback combination failed");
                return "<!-- Translation completed with errors - some content may be missing -->";
            }
        }
    }
}
