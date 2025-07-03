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
                _logger.LogInformation("Processing page {PageNumber} with {SectionCount} sections", page.Page, page.ScreenShots.Count);
                var pageTranslations = new List<string>();
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

                    if (string.IsNullOrWhiteSpace(base64Data))
                    {
                        _logger.LogWarning("Empty screenshot data for page {Page} section {Section}", page.Page, i);
                        pageTranslations.Add(string.Empty);
                        continue;
                    }

                    try
                    {
                        var imageData = Convert.FromBase64String(base64Data);

                        var translated = await ExtractAndTranslateWithClaude(imageData, page.Page, sectionName, targetLanguage.Name, model, cancellationToken);
                        pageTranslations.Add(translated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process page {Page} section {Section} ({SectionName})", page.Page, i, sectionName);
                        pageTranslations.Add(string.Empty);
                    }
                }

                translatedPages.Add(pageTranslations);
            }

            var totalTranslatedSections = translatedPages.Sum(p => p.Count(s => !string.IsNullOrWhiteSpace(s)));
            _logger.LogInformation("Successfully translated {TranslatedSections} sections out of {TotalSections} total sections", 
                totalTranslatedSections, translatedPages.Sum(p => p.Count));

            if (totalTranslatedSections == 0)
            {
                return new DocumentTranslationResult 
                { 
                    Success = false, 
                    ErrorMessage = "No content could be extracted and translated from any section of the document" 
                };
            }

            var finalMarkdown = await CombineTranslatedSectionsWithClaude(translatedPages, targetLanguage.Name, model, cancellationToken);
            
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
                _logger.LogInformation("Extracting and translating text from page {PageNumber} {SectionName} section with Claude", pageNumber, sectionName);
        
                string base64Image = Convert.ToBase64String(imageData);
        
                var messages = new List<ContentFile>
                {
                    new ContentFile 
                    { 
                        Type = "text", 
                        Text = ExtractTextAndTranslate(pageNumber, sectionName, targetLanguageName) 
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
        
                var claudeResponse = await _aiService.SendRequestWithFile(messages, model, cancellationToken);
        
                var content = claudeResponse.Content;
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude for page {PageNumber} {SectionName} section", pageNumber, sectionName);
                    return string.Empty;
                }
        
                string translatedText = content.Trim();
        
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    _logger.LogWarning("Claude returned empty result for page {PageNumber} {SectionName} section", pageNumber, sectionName);
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
        
                _logger.LogInformation("Successfully extracted and translated {Length} characters from page {PageNumber} {SectionName} section", 
                    translatedText.Length, pageNumber, sectionName);
        
                return translatedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting and translating text with Claude from page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }
        }
        private async Task<string> CombineTranslatedSectionsWithClaude(List<List<string>> translatedPageSections, string targetLanguageName, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Combining translated sections from {PageCount} pages using a page-by-page approach", translatedPageSections.Count);

                var translatedPages = new List<string>();

                for (int i = 0; i < translatedPageSections.Count; i++)
                {
                    var pageSections = translatedPageSections[i];
                    if (pageSections == null || pageSections.All(string.IsNullOrWhiteSpace))
                    {
                        _logger.LogWarning("Skipping page {PageNumber} due to empty sections.", i + 1);
                        continue;
                    }

                    _logger.LogInformation("Combining {SectionCount} sections for page {PageNumber}", pageSections.Count, i + 1);
                    
                    var combinedPage = await CombineSectionsForSinglePage(pageSections, targetLanguageName, model, i + 1, cancellationToken);
                    translatedPages.Add(combinedPage);
                }

                if (translatedPages.Count == 0)
                {
                    _logger.LogWarning("All pages resulted in empty content after combination.");
                    return string.Empty;
                }

                var finalDocument = string.Join("\n\n<hr />\n\n", translatedPages);
                _logger.LogInformation("Successfully combined {PageCount} pages into a final document of length {Length}",
                    translatedPages.Count, finalDocument.Length);

                return finalDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error combining translated sections with Claude");
        
                var simpleCombination = new StringBuilder();
                foreach (var pageSections in translatedPageSections)
                {
                    foreach (var section in pageSections)
                    {
                        simpleCombination.AppendLine(section);
                        simpleCombination.AppendLine();
                    }
                }
        
                return simpleCombination.ToString();
            }
        }

        private async Task<string> CombineSectionsForSinglePage(List<string> pageSections, string targetLanguageName, AIModel model, int pageNumber, CancellationToken cancellationToken)
        {
            var sectionsContent = new StringBuilder();
            for (int i = 0; i < pageSections.Count; i++)
            {
                sectionsContent.AppendLine($"SECTION {i + 1}:\n{pageSections[i]}\n");
            }

            var prompt = GenerateSinglePageCombinationPrompt(targetLanguageName, sectionsContent.ToString());

            var message = new ContentFile { Type = "text", Text = prompt };
            var response = await _aiService.SendRequestWithFile([message], model, cancellationToken);

            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("Failed to combine sections for page {PageNumber}. AI service returned an error or empty content. Error: {ErrorMessage}", pageNumber, response.ErrorMessage);
                // Fallback to concatenating the sections for this page
                return string.Join("\n\n", pageSections);
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
            
            _logger.LogInformation("Successfully combined sections for page {PageNumber}, result length: {Length}", pageNumber, combinedPage.Length);
            return combinedPage;
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
                    You are an expert multilingual document translator and formatter.
                    You specialize in converting OCR-extracted text into professionally polished Markdown documents in {language.Name}.
                </role>

                <context>
                    You will be provided with a raw text chunk extracted via OCR from a PDF document.

                    Chunk ID: {i + 1} of {chunks.Count}
                </context>

                <task>
                    Translate the provided text into **{language.Name}**, precisely following the instructions below.
                </task>

                <instructions>
                    1. <Full Translation>
                        Translate **all** textual content into {language.Name}. Nothing translatable should be omitted.

                    2. <Layout Preservation>
                        Reproduce the **original layout and structure** using **Markdown**. Preserve paragraph breaks, meaningful line breaks, and the visual hierarchy.

                    3. <Data Integrity>
                        Preserve all **numbers, dates, general codes, and identifiers** in their original form or transliterate as appropriate for {language.Name}.

                    4. <Non-Translatable Elements>
                        The following must **not be translated** and must appear **exactly as in the source**:
                        - Technical identifiers (e.g., part numbers, model numbers)
                        - Official standards (e.g., ISO 9001, ДСТУ Б В.2.7-170:2008)
                        - Specific codes (e.g., EAN codes, НААУ, ДФРПОУ)
                        - Reference numbers

                    5. <OCR Artifact Handling>
                        - Detect and correct **common OCR errors** (e.g., garbled characters, misreads).
                        - If uncertain, prefer a faithful transcription over guessing.

                    6. <Structural Fidelity>
                        Maintain the original structure:
                        - Paragraph breaks
                        - Line spacing (use `\n` or Markdown formatting)
                        - Sequence of sections

                    7. <Proper Nouns>
                        Transliterate proper names (people, organizations, places) per {language.Name} norms, unless an accepted translation exists.

                    8. <Technical Terminology>
                        Use **standard technical terms** in {language.Name} relevant to the document's subject matter.

                    9. <Duplicate Text>
                        Preserve and translate **all repetitions**. Do not deduplicate.

                    10. <HTML Formatting Rules>
                        Format the translated text using **strict HTML tags only**. **Do not use any Markdown formatting** under any circumstances.

                        Use the following HTML elements appropriately:
                        - Headings: `<h1>`, `<h2>`, `<h3>`, etc.
                        - Paragraphs: `<p>`
                        - Lists: `<ul>` / `<ol>` with `<li>` items
                        - Tables: `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`
                        - Code blocks or preformatted text: `<pre>` or `<code>`
                        - Section breaks: Use `<hr />`
                        - Line breaks: Use `<br />` where meaningful line breaks are needed

                        Ensure the HTML is valid and clean. Nest tags properly, avoid inline styles, and do **not** include any raw Markdown syntax.

                </instructions>

                <output_constraints>
                    - Output **only** the final translated text in {language.Name}
                    - Do **not** include English explanations, comments, or the original text
                    - Do **not** skip or summarize any content unless marked non-translatable
                    - Your entire response must be in clean HTML
                </output_constraints>

                <ocr_input>
                    Here is the OCR-extracted text (chunk {i + 1} of {chunks.Count}):

                    {chunk}
                </ocr_input>

                ";
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
                        - Paragraphs: `<p>`
                        - Line breaks: `<br />`

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
    
        private static string GenerateFinalPrompt(string targetLanguageName, string finalCombination)
        {
            return $@"
                <role>
                    You are an expert document assembler and HTML formatting specialist.
                    You have multiple translated text chunks that must be meticulously combined into a single, coherent, and well-structured document in {targetLanguageName}.
                </role>

                <task>
                    1. <Combine Chunks>
                        Integrate all provided text chunks into one seamless document.

                    2. <Seamless Integration>
                        The chunks are sequential parts of a single document. Combine them seamlessly, ensuring a natural flow between them. It is critical that no content is lost at the boundaries of the chunks.

                    3. <Ensure Cohesion and Flow>
                        The final document should flow naturally and logically with smooth transitions between sections.

                    4. <Preserve and Enhance HTML>
                        - Preserve all existing HTML formatting (headings, lists, tables, code blocks, emphasis, etc.).
                        - Based on the overall context and structure, apply or refine HTML tags and structure to improve readability and professional presentation (e.g., add missing headings <h1>, <h2>, properly nest lists, insert <hr /> for clear section breaks).

                    5. <Maintain Content Integrity>
                        Do NOT add any new textual content, comments, or remarks not present in the original chunks.

                    6. <Final Output>
                        Return ONLY the final combined and polished document in {targetLanguageName}, formatted entirely using valid and clean HTML.
                        No explanations or additional text before or after the document.
                </task>

                <input>
                    Translated chunks to combine and refine:

                    {finalCombination}
                </input>

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
    }
}
