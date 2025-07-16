using System;
using System.Collections.Generic;
using System.IO;
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

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class WordProcessor(
    IAIService aiService,
    ILanguageService languageService, 
    IUserService userService, 
    IGptService gptService,
    ILogger<DocumentTranslationService> logger
) : IWordProcessor
{
    
    private readonly IAIService _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    private readonly IGptService _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
    private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    public bool CanProcess(string fileExtension)
    {
        if (string.IsNullOrWhiteSpace(fileExtension))
            return false;
            
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".docx" or ".doc";
    }
    
    public Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    
    public async Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, AIModel model, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userService.GetById(userId, cancellationToken);
            if (user == null)
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "User not found" };
            }
            
            if (file == null)
                return new DocumentTranslationResult { Success = false, ErrorMessage = "File is null" };
                
            if (file.Length == 0)
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Uploaded file is empty" };

            if (string.IsNullOrWhiteSpace(userId))
                return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };

            _logger.LogInformation("Starting Word document translation for file: {FileName}, Size: {FileSize}, Target Language: {LanguageId}", 
                file.FileName, file.Length, targetLanguageId);

            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);

            if (targetLanguage == null)
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Target language with ID {targetLanguageId} not found" };
                
            if (string.IsNullOrWhiteSpace(targetLanguage.Name))
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language name is empty" };

            var screenshotResult = await GetWordDocumentScreenshots(file, cancellationToken);
            
            if (screenshotResult == null)
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Failed to generate document screenshots" };
                
            if (screenshotResult.Pages == null || !screenshotResult.Pages.Any())
                return new DocumentTranslationResult { Success = false, ErrorMessage = "No pages found in document screenshots" };

            _logger.LogInformation("Received {PageCount} pages from screenshot service, method: {Method}", 
                screenshotResult.Pages.Count, screenshotResult.Method);
            
            foreach (var page in screenshotResult.Pages)
            {
                _logger.LogInformation("Page {Page}: Screenshots={ScreenshotCount}, Text length={TextLength}", 
                    page.Page, 
                    page.ScreenShots?.Count ?? 0,
                    page.Text?.Length ?? 0);
            }

            var translatedPages = new List<List<string>>();

            foreach (var page in screenshotResult.Pages)
            {
                var pageTranslations = new List<string>();
                
                for (int i = 0; i < page.ScreenShots.Count; i++)
                {
                    var base64Data = page.ScreenShots[i];
                    var sectionName = $"section-{i + 1}";

                    if (string.IsNullOrWhiteSpace(base64Data))
                    {
                        _logger.LogWarning("Empty screenshot data for page {Page} section {Section}", page.Page, i);
                        pageTranslations.Add(string.Empty);
                        continue;
                    }

                    // Use retry mechanism for failed sections
                    var translatedSection = await TranslateWordSectionWithRetry(base64Data, page.Page, sectionName, targetLanguage.Name, model, cancellationToken, page.Text);
                    pageTranslations.Add(translatedSection ?? string.Empty);
                    
                    _logger.LogInformation("Word document page {Page} section {Section} translation result: {Length} chars", 
                        page.Page, i, translatedSection?.Length ?? 0);
                }

                translatedPages.Add(pageTranslations);
            }

            var totalSections = translatedPages.Sum(p => p.Count);
            var translatedSections = translatedPages.Sum(p => p.Count(s => !string.IsNullOrWhiteSpace(s)));
            var pagesWithContent = translatedPages.Count(p => p.Any(s => !string.IsNullOrWhiteSpace(s)));
            
            _logger.LogInformation("Word document translation summary: {TranslatedSections}/{TotalSections} sections translated across {PagesWithContent}/{TotalPages} pages", 
                translatedSections, totalSections, pagesWithContent, translatedPages.Count);

            // Only fail if absolutely no content was extracted from any page
            if (translatedSections == 0)
                return new DocumentTranslationResult { Success = false, ErrorMessage = "No content could be extracted from the document" };

            // Warn if significant data loss occurred
            var dataLossPercentage = (double)(totalSections - translatedSections) / totalSections * 100;
            if (dataLossPercentage > 25)
            {
                _logger.LogWarning("Significant data loss detected in Word document: {DataLossPercentage:F1}% of sections failed translation", dataLossPercentage);
            }

            var finalMarkdown = await CombineTranslatedWordSectionsWithClaude(translatedPages, targetLanguage.Name, model, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(finalMarkdown))
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Failed to combine translated sections" };
            
            string improvedTranslation = finalMarkdown;
            double qualityScore = 0.0;
    
            if (!string.IsNullOrWhiteSpace(finalMarkdown))
            {
                _logger.LogInformation("Starting translation verification for Claude-translated Word document");
        
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

                    var (verificationResult, verifiedTranslation) = await ProcessTranslationVerification(
                        cancellationToken, translationChunksForVerification, targetLanguage, finalMarkdown);
            
                    if (verificationResult?.Success == true)
                    {
                        improvedTranslation = verifiedTranslation ?? finalMarkdown;
                        qualityScore = verificationResult.QualityScore ?? 1.0;
                        _logger.LogInformation("Claude Word document translation verification completed with score: {Score}", qualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("Translation verification failed: {Error}", verificationResult?.ErrorMessage ?? "Unknown error");
                        qualityScore = 0.5;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during Claude Word document translation verification, using original translation");
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
            else
            {
                if (!string.IsNullOrEmpty(translationResult.TranslatedContent))
                {
                    translationResult.FileData = Encoding.UTF8.GetBytes(translationResult.TranslatedContent);
                    translationResult.FileName = $"translated_{translationResult.TranslationId}.md";
                    translationResult.ContentType = "text/markdown";
                }
            }

            _logger.LogInformation("Word document translation completed successfully. Translation ID: {TranslationId}, Quality Score: {QualityScore}", 
                translationId, qualityScore);

            return translationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TranslateWordDocumentWithClaude");
            return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error in Word document translation workflow: {ex.Message}" };
        }
    }
    
    public async Task<int> CountPagesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Cannot count pages: file is null or empty");
                return 0;
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!CanProcess(fileExtension))
            {
                _logger.LogWarning("Cannot count pages: unsupported file extension {Extension}", fileExtension);
                throw new ArgumentException($"Unsupported file extension: {fileExtension}");
            }

            _logger.LogInformation("Counting pages for Word document: {FileName}", file.FileName);

            var screenshotResult = await GetWordDocumentScreenshots(file, cancellationToken);
            
            if (screenshotResult?.Pages == null)
            {
                _logger.LogWarning("Failed to get pages from Word document processing service");
                return 0;
            }

            var pageCount = screenshotResult.Pages.Count;
            _logger.LogInformation("Word document {FileName} has {PageCount} pages", file.FileName, pageCount);
            
            return pageCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting pages in Word document {FileName}", file.FileName);
            throw;
        }
    }

    private async Task<ScreenShotResult> GetWordDocumentScreenshots(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Uploaded file is empty or null", nameof(file));

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5); 
        
        await using var fileStream = file.OpenReadStream();
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(streamContent, "file", file.FileName);
        
        HttpResponseMessage response;
        try
        {
            _logger.LogInformation("Sending document to screenshot service: {FileName}", file.FileName);
            response = await httpClient.PostAsync("http://127.0.0.1:8000/process-word", content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to contact Word document screenshot service");
            throw new InvalidOperationException("Failed to contact Word document screenshot service", ex);
        }

        var result = await response.Content.ReadFromJsonAsync<ScreenShotResult>(cancellationToken: cancellationToken);
        if (result == null)
        {
            _logger.LogError("Word document processing service returned null result");
            throw new InvalidOperationException("Word document processing service returned null");
        }

        if (result.Pages == null || result.Pages.Count == 0)
        {
            _logger.LogError("Word document processing returned no pages");
            throw new InvalidOperationException("No pages found in document processing result");
        }

        _logger.LogInformation("Successfully processed document using {Method} with {PageCount} pages", 
            result.Method ?? "screenshot", result.Pages.Count);
        return result;
    }

    private async Task<string> ExtractAndTranslateWordDocumentWithClaude(byte[] imageData, int pageNumber, string sectionName, string targetLanguageName, AIModel model, CancellationToken cancellationToken, string extractedText = null)
    {
        try
        {
            _logger.LogInformation("Extracting and translating text from Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
    
            var messages = new List<ContentFile>();
            
            if (!string.IsNullOrEmpty(extractedText))
            {
                _logger.LogInformation("Using pre-extracted text for translation (length: {Length})", extractedText.Length);
                messages.Add(new ContentFile 
                { 
                    Type = "text", 
                    Text = $"Translate the following Word document text to {targetLanguageName}. Preserve all formatting and structure:\n\n{extractedText}"
                });
            }
            else if (imageData != null && imageData.Length > 0)
            {
                _logger.LogInformation("Using screenshot data for OCR and translation");
                string base64Image = Convert.ToBase64String(imageData);
        
                messages.Add(new ContentFile 
                { 
                    Type = "text", 
                    Text = ExtractWordDocumentTextAndTranslate(pageNumber, sectionName, targetLanguageName) 
                });
                messages.Add(new ContentFile 
                { 
                    Type = "image", 
                    Source = new Source()
                    {
                        Type = "base64",
                        MediaType = "image/png",
                        Data = base64Image
                    }
                });
            }
            else
            {
                _logger.LogWarning("No image data or extracted text available for page {PageNumber} {SectionName}", pageNumber, sectionName);
                return string.Empty;
            }

            _logger.LogInformation("Sending Word document image to {Model} for translation", model);
            var aiResponse = await _aiService.SendRequestWithFile(messages, model, cancellationToken);
    
            if (!aiResponse.Success)
            {
                _logger.LogWarning("No content received from AI service for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(aiResponse.Content))
            {
                _logger.LogWarning("AI service returned null or empty text for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }
    
            string translatedText = aiResponse.Content.Trim();
    
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogWarning("Claude returned empty result for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }
    
            translatedText = CleanAiResponsePrefix(translatedText);
    
            _logger.LogInformation("Successfully extracted and translated {Length} characters from Word document page {PageNumber} {SectionName} section", 
                translatedText.Length, pageNumber, sectionName);
    
            return translatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting and translating text with Claude from Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
            return string.Empty;
        }
    }
    
    private async Task<(TranslationVerificationResult, string)> ProcessTranslationVerification(
        CancellationToken cancellationToken, 
        List<KeyValuePair<int, string>> translationChunks, 
        LanguageModel targetLanguage, 
        string originalTranslation)
    {
        try
        {
            if (translationChunks == null || !translationChunks.Any())
            {
                _logger.LogWarning("No translation chunks provided for verification");
                return (new TranslationVerificationResult { Success = false, ErrorMessage = "No chunks to verify" }, originalTranslation);
            }

            if (targetLanguage == null)
            {
                _logger.LogWarning("Target language is null during verification");
                return (new TranslationVerificationResult { Success = false, ErrorMessage = "Target language is null" }, originalTranslation);
            }

            _logger.LogInformation("Starting translation verification for {Count} chunks", translationChunks.Count);

            var verificationResult = await _gptService.VerifyTranslationBatch(
                translationChunks, cancellationToken);
                    
            _logger.LogInformation("Translation verification completed. Success: {Success}, Score: {Score}, Verified: {Verified}/{Total}", 
                verificationResult.Success, 
                verificationResult.QualityScore,
                verificationResult.VerifiedChunks,
                translationChunks.Count);
                    
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
                    originalTranslation, targetLanguage.Name, verificationResult.Feedback, cancellationToken);
                        
                if (!string.IsNullOrEmpty(improvedTranslation))
                {
                    _logger.LogInformation("Applied GPT's suggestions to improve translation");
                    originalTranslation = improvedTranslation;
                            
                    _logger.LogInformation("Re-verifying improved translation...");
                        
                    var improvedVerification = await _gptService.EvaluateTranslationQuality(
                        $"Evaluate this translation to {targetLanguage.Name}:\n\n{improvedTranslation}", 
                        cancellationToken);
                            
                    if (improvedVerification.Success)
                    {
                        _logger.LogInformation("Improved translation verification score: {Score} (was: {OldScore})", 
                            improvedVerification.QualityScore, verificationResult.QualityScore);
                        verificationResult = improvedVerification;
                    }
                }
            }

            var translationVerificationResult = new TranslationVerificationResult
            {
                Success = verificationResult.Success,
                QualityScore = verificationResult.QualityScore,
                ErrorMessage = verificationResult.ErrorMessage
            };

            return (translationVerificationResult, originalTranslation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessTranslationVerification");
            return (new TranslationVerificationResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message 
            }, originalTranslation ?? string.Empty);
        }
    }

    private async Task<string> ImproveTranslationWithFeedback(string translatedText, string targetLanguage, string feedback, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempting to improve translation based on GPT feedback");
                
            var prompt = GenerateImprovedTranslationPrompt(translatedText, targetLanguage, feedback);

            var cachedSystemPrompt = ClaudeService.CreateCachedSystemPrompt(
                $"You are a translation improvement specialist for {targetLanguage}. " +
                $"Your task is to enhance translations based on feedback while maintaining accuracy."
            );
                
            var message = new ContentFile { Type = "text", Text = prompt };
                
            _logger.LogInformation("Sending improvement request to AI service");
            var aiResponse = await _aiService.SendRequestWithFile([message], AIModel.Claude4Sonnet, cancellationToken);
                
            if (!aiResponse.Success)
            {
                _logger.LogWarning("No content received from AI service for translation improvement");
                return string.Empty;
            }
                
            string improvedTranslation = aiResponse.Content.Trim();
                
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


    private async Task<string> CombineTranslatedWordSectionsWithClaude(List<List<string>> translatedPageSections, string targetLanguageName, AIModel model, CancellationToken cancellationToken)
    {
        try
        {
            if (translatedPageSections == null || !translatedPageSections.Any())
            {
                _logger.LogWarning("No translated page sections to combine");
                return string.Empty;
            }

            _logger.LogInformation("Combining translated Word document sections from {PageCount} pages using Claude", translatedPageSections.Count);
    
            var allSections = new List<string>();
            for (int i = 0; i < translatedPageSections.Count; i++)
            {
                var pageSections = translatedPageSections[i];
                if (pageSections != null)
                {
                    allSections.AddRange(pageSections
                        .Where(section => !string.IsNullOrWhiteSpace(section))
                        .Select((section, idx) => 
                            $"PAGE {i + 1} - SECTION {idx + 1} ({GetSectionPosition(idx)}):\n\n{section}"));
                }
            }

            if (!allSections.Any())
            {
                _logger.LogWarning("No valid sections found to combine");
                return string.Empty;
            }
    
            var chunks = new List<List<string>>();
            var currentChunk = new List<string>();
            int currentLength = 0;
            const int maxChunkLength = 80000;
    
            foreach (var section in allSections)
            {
                if (currentLength + section.Length > maxChunkLength && currentChunk.Any())
                {
                    chunks.Add([..currentChunk]);
                    currentChunk.Clear();
                    currentLength = 0;
                }
        
                currentChunk.Add(section);
                currentLength += section.Length;
            }
    
            if (currentChunk.Any())
            {
                chunks.Add(currentChunk);
            }
    
            _logger.LogInformation("Split Word document sections into {ChunkCount} chunks for combination", chunks.Count);
    
            var combinedChunks = new List<string>();
    
            for (int i = 0; i < chunks.Count; i++)
            {
                var combinedChunk = await ProcessChunkCombination(chunks[i], targetLanguageName, i + 1, chunks.Count, model, cancellationToken);
                if (!string.IsNullOrWhiteSpace(combinedChunk))
                {
                    combinedChunks.Add(combinedChunk);
                }
            }
    
            if (combinedChunks.Count > 1)
            {
                _logger.LogInformation("Combining {Count} final Word document chunks", combinedChunks.Count);
                return await ProcessFinalCombination(combinedChunks, targetLanguageName, cancellationToken);
            }
    
            return combinedChunks.FirstOrDefault() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error combining translated Word document sections with Claude");
            return CreateFallbackCombination(translatedPageSections);
        }
    }

    private async Task<string> ProcessChunkCombination(List<string> chunk, string targetLanguageName, int chunkIndex, int totalChunks, AIModel model, CancellationToken cancellationToken)
    {
        try
        {
            var chunkContent = string.Join("\n\n===== SECTION SEPARATOR =====\n\n", chunk);
    
            _logger.LogInformation("Processing Word document chunk {ChunkIndex}/{ChunkCount} with {SectionCount} sections, size: {Size} characters", 
                chunkIndex, totalChunks, chunk.Count, chunkContent.Length);
    
            var prompt = GenerateWordDocumentCombinationPrompt(targetLanguageName, chunkContent);

            var message = new ContentFile { Type = "text", Text = prompt };
    
            var aiResponse = await _aiService.SendRequestWithFile([message], model, cancellationToken);
    
            if (!aiResponse.Success)
            {
                _logger.LogWarning("No content received from AI service for combining Word document chunk {ChunkIndex}", chunkIndex);
                return string.Join("\n\n", chunk);
            }
    
            string combinedChunk = CleanAiResponsePrefix(aiResponse.Content.Trim());
            
            if (string.IsNullOrWhiteSpace(combinedChunk))
            {
                _logger.LogWarning("Claude returned empty result for combining Word document chunk {ChunkIndex}", chunkIndex);
                return string.Join("\n\n", chunk);
            }
    
            _logger.LogInformation("Successfully combined Word document chunk {ChunkIndex}, result length: {Length} characters", 
                chunkIndex, combinedChunk.Length);
    
            return combinedChunk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chunk {ChunkIndex} combination", chunkIndex);
            return string.Join("\n\n", chunk);
        }
    }

    private async Task<string> ProcessFinalCombination(List<string> combinedChunks, string targetLanguageName, CancellationToken cancellationToken)
    {
        try
        {
            var finalCombination = string.Join("\n\n", combinedChunks);
            var finalPrompt = GenerateWordDocumentFinalPrompt(targetLanguageName, finalCombination);

            // Create cached system prompt for final document processing
            var cachedSystemPrompt = ClaudeService.CreateCachedSystemPrompt(
                $"You are a professional document editor for {targetLanguageName} publications. " +
                $"Your task is to create the final, polished version with guaranteed duplicate removal."
            );
    
            var finalMessage = new ContentFile { Type = "text", Text = finalPrompt };
    
            var finalResponse = await _aiService.SendRequestWithFile([finalMessage], AIModel.Claude4Sonnet, cancellationToken);
    
            if (finalResponse.Success)
            {
                string finalResult = CleanAiResponsePrefix(finalResponse.Content.Trim());
        
                if (!string.IsNullOrWhiteSpace(finalResult))
                {
                    _logger.LogInformation("Successfully combined all Word document chunks into final document, length: {Length} characters", finalResult.Length);
                    return finalResult;
                }
            }
    
            _logger.LogWarning("Failed to combine final Word document chunks, returning concatenated chunks");
            return finalCombination;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in final combination processing");
            return string.Join("\n\n", combinedChunks);
        }
    }

    private string CreateFallbackCombination(List<List<string>> translatedPageSections)
    {
        var simpleCombination = new StringBuilder();
        
        if (translatedPageSections != null)
        {
            foreach (var pageSections in translatedPageSections)
            {
                if (pageSections != null)
                {
                    foreach (var section in pageSections.Where(s => !string.IsNullOrWhiteSpace(s)))
                    {
                        simpleCombination.AppendLine(section);
                        simpleCombination.AppendLine();
                    }
                }
            }
        }

        return simpleCombination.ToString();
    }

    private static string GetSectionPosition(int index) => index switch
    {
        0 => "TOP",
        1 => "MIDDLE", 
        _ => "BOTTOM"
    };

    private static string CleanAiResponsePrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = CleanGptResponse(text);

        var prefixes = new[]
        {
            "Here", "I've translated", "The translated", "I've combined", "Here's the",
            "I've", "Here is", "The following", "Below is", "Here's", "Translation:",
            "Translated text:", "Combined document:", "Final document:", "Improved translation:",
            "Here is the translation:", "Here is the combined document:", "Here is the final document:",
            "Here is the improved translation:", "The translation is:", "The combined document is:",
            "The final document is:", "The improved translation is:", "Here's the translation:",
            "Here's the combined document:", "Here's the final document:", "Here's the improved translation:",
            "Could not parse rating from response:", "Error:", "Warning:", "Note:", "Response:"
        };

        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                int firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak > 0)
                {
                    text = text.Substring(firstLineBreak + 1).Trim();
                }
                else
                {
                    text = text.Substring(prefix.Length).Trim();
                    if (text.StartsWith(":"))
                    {
                        text = text.Substring(1).Trim();
                    }
                }
                break;
            }
        }

        text = text.Replace("Translation:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Combined document:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Final document:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Improved translation:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Could not parse rating from response:", "", StringComparison.OrdinalIgnoreCase)
                  .Trim();

        return text;
    }

    private static string CleanGptResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove GPT-specific error messages and prefixes
        var gptPrefixes = new[]
        {
            "Could not parse rating from response:",
            "Error parsing response:",
            "Failed to parse:",
            "Unable to parse:",
            "Parse error:",
            "Rating error:",
            "Response error:"
        };

        foreach (var prefix in gptPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                int firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak > 0)
                {
                    text = text.Substring(firstLineBreak + 1).Trim();
                }
                else
                {
                    text = text.Substring(prefix.Length).Trim();
                }
                break;
            }
        }

        // Remove any remaining GPT-specific patterns
        text = text.Replace("Could not parse rating from response:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Error parsing response:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Failed to parse:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Unable to parse:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Parse error:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Rating error:", "", StringComparison.OrdinalIgnoreCase)
                  .Replace("Response error:", "", StringComparison.OrdinalIgnoreCase)
                  .Trim();

        return text;
    }
    
    private List<string> GetChunksOfText(string text, int maxChunkSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (text.Length <= maxChunkSize)
            return [text];

        var chunks = new List<string>();
        var sentences = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
    
        var currentChunk = new StringBuilder();
    
        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence))
                continue;
            
            var sentenceWithPunctuation = trimmedSentence;
            if (!trimmedSentence.EndsWith('.') && !trimmedSentence.EndsWith('!') && !trimmedSentence.EndsWith('?'))
                sentenceWithPunctuation += ".";
            
            if (currentChunk.Length + sentenceWithPunctuation.Length + 1 > maxChunkSize)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
            
                if (sentenceWithPunctuation.Length > maxChunkSize)
                {
                    var words = sentenceWithPunctuation.Split(' ');
                    var wordChunk = new StringBuilder();
                
                    foreach (var word in words)
                    {
                        if (wordChunk.Length + word.Length + 1 > maxChunkSize)
                        {
                            if (wordChunk.Length > 0)
                            {
                                chunks.Add(wordChunk.ToString().Trim());
                                wordChunk.Clear();
                            }
                        }
                    
                        if (wordChunk.Length > 0)
                            wordChunk.Append(" ");
                        wordChunk.Append(word);
                    }
                
                    if (wordChunk.Length > 0)
                        currentChunk.Append(wordChunk.ToString());
                }
                else
                {
                    currentChunk.Append(sentenceWithPunctuation);
                }
            }
            else
            {
                if (currentChunk.Length > 0)
                    currentChunk.Append(" ");
                currentChunk.Append(sentenceWithPunctuation);
            }
        }
    
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString().Trim());
    
        return chunks;
    }
    private static string ExtractWordDocumentTextAndTranslate(int pageNumber, string sectionName, string targetLanguageName)
    {
        string verticalPortion = sectionName switch
        {
            "top" => "0-50%",
            "middle" => "25-75%",
            "bottom" => "50-100%",
            _ => "full page"
        };

        return $"""
                You are an advanced OCR (Optical Character Recognition) and translation system specialized in processing Microsoft Word documents.
                Your task is to process an image snippet from a Word document.

                **Context:**
                - Page: {pageNumber}
                - Section: {sectionName} ({verticalPortion})
                - Target Language: {targetLanguageName}

                **Required Actions:**
                1.  **Extract Text**: Accurately extract ALL visible textual content from the provided Word document image section, including:
                * Headers and footers if visible
                * Body text and paragraphs
                * Bullet points and numbered lists
                * Table content if present
                * Any captions or annotations
                2.  **Translate**: Translate the entire extracted text into **{targetLanguageName}**.
                3.  **Format as HTML**: Present the translated content using **strict HTML tags only**. Markdown is prohibited.
                * Headings: Use `<h1>`, `<h2>`, `<h3>`, etc. (e.g., `<h1>Main Heading</h1>`, `<h2>Subheading</h2>`).
                * Lists: Use `<ul>`, `<ol>`, with `<li>` - **Use bullet lists when content naturally fits list format**.
                * Emphasis: Use `<strong>` for bold and `<em>` for italic.
                * Tables: Use `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`.
                * Separators: Use `<hr />` to logically separate distinct content sections where appropriate.
                * Code/Technical Blocks: Use `<pre>`, `<code>` for code snippets or highly structured technical content.
                4.  **Preserve Original Data**:
                * Keep all numbers, dates, and specific codes exactly as they appear in the original, or transliterate them appropriately if they are part of a sentence structure that requires it in {targetLanguageName}.
                * Maintain any special formatting cues visible in the Word document (indentation, spacing).
                5.  **CRITICAL - Non-Translation Rules**:
                * **DO NOT TRANSLATE** the following items:
                * Technical identifiers, model numbers, part numbers.
                * Standards (e.g., ISO, EN, ÃÃÃÃÃ, ÃÃÃÃÃ).
                * Specific codes (e.g., ÃÃÂ¤Ã ÃÃÃÂ£, ÃÃÃÃÂ£).
                * Reference numbers.
                * **EMAIL ADDRESSES** - Never translate email addresses, keep them exactly as they appear.
                * These items must be preserved in their original form.
                6.  **Proper Nouns and Human Names**: 
                **HUMAN NAMES**: When encountering human names (first names, last names, full names), always use TRANSLITERATION rather than translation - convert the name to {targetLanguageName} script/alphabet while preserving the original pronunciation.
                Examples: "John Smith" → transliterate to {targetLanguageName} script, "María González" → transliterate to {targetLanguageName} script
                Transliterate other proper nouns (organizations, specific places) according to standard {targetLanguageName} conventions if a common translation doesn't exist.
                7.  **Word Document Structure**: Pay special attention to Word-specific elements:
                * Document titles and subtitles
                * Section breaks and page breaks
                * Footnotes or endnotes if visible
                * Any tracked changes or comments (if visible)

                **Output:**
                - ONLY the translated text in {targetLanguageName}
                - NO explanations or comments
                - NO prefixes like "Translation:" or "Here is:"
                - Start directly with the translated content
                """;
    }

    private static string GenerateVerificationPrompt(string targetLanguage, string translatedText)
    {
        return $"""
                You are a professional translation quality reviewer.

                **Task**: Review and improve the following {targetLanguage} translation.

                **Review Guidelines**:
                1. Check for grammatical accuracy and natural flow
                2. Ensure technical terms are handled correctly
                3. Verify that formatting and structure are preserved
                4. Improve clarity and readability while maintaining meaning
                5. Keep all codes, standards, reference numbers, and email addresses unchanged
                6. Maintain professional document tone

                **Translation to review**:

                {translatedText}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the improved translation in {targetLanguage}
                - Format ONLY in HTML using strict HTML tags
                - Do NOT include explanations, comments, or reasoning
                - Do NOT include phrases like "Improved translation:", "Here is:", etc.
                - Start your response directly with the improved translated content
                """;
    }


    private static string GenerateGptVerificationPrompt(string targetLanguage, string translatedText)
    {
        return $"""
                You are an expert translation quality reviewer.

                **Task**: Review and improve the following {targetLanguage} translation.

                **Review Guidelines**:
                1. Check for grammatical accuracy and natural flow
                2. Ensure technical terms are handled correctly
                3. Verify that formatting and structure are preserved
                4. Improve clarity and readability while maintaining meaning
                5. Keep all codes, standards, reference numbers, and email addresses unchanged
                6. Maintain professional document tone

                **Translation to review**:

                {translatedText}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the improved translation in {targetLanguage}
                - Format ONLY in HTML using strict HTML tags
                - Do NOT include any explanations, comments, or reasoning
                - Do NOT include phrases like "Improved translation:", "Here is:", etc.
                - Start your response directly with the improved translated content
                """;
    }

    private static string GenerateWordDocumentCombinationPrompt(string targetLanguageName, string sectionsToCompine)
    {
        return $"""
                You are an expert document assembly specialist for Word document translations.

                **CRITICAL TASK**: Assemble the following translated sections into a properly ordered {targetLanguageName} document.

                **DOCUMENT STRUCTURE RULES**:
                1. **MAINTAIN ORIGINAL ORDER**: Sections are labeled "PAGE X - SECTION Y" - preserve this sequential order
                2. **LOGICAL FLOW**: Combine sections from PAGE 1 → PAGE 2 → PAGE 3, etc.
                3. **SECTION SEQUENCE**: Within each page, combine TOP → MIDDLE → BOTTOM sections
                4. **REMOVE LABELS**: Strip all "PAGE X - SECTION Y" markers from the final output
                5. **PRESERVE HIERARCHY**: Maintain heading levels and document structure
                6. **SEAMLESS TRANSITIONS**: Create natural flow between pages without gaps or redundancy

                **DUPLICATE CONTENT ELIMINATION**:
                - **CRITICAL**: Remove any duplicate sentences, paragraphs, or content blocks
                - If the same text appears in multiple sections, include it only ONCE in the logical position
                - Pay special attention to overlapping content between adjacent sections (TOP/MIDDLE, MIDDLE/BOTTOM)
                - When content appears multiple times, keep the first occurrence and remove subsequent duplicates
                - Check for identical or nearly identical sentences and merge them appropriately

                **CONTENT HANDLING**:
                - Keep ALL technical information, codes, and references exactly as translated
                - Maintain table structures and formatting
                - Preserve bullet points and numbered lists in order
                - Ensure headings follow logical hierarchy (H1 → H2 → H3)
                - Remove duplicate headers that appear across page boundaries
                - Maintain paragraph spacing and document flow

                **FORMATTING REQUIREMENTS**:
                - Output in clean, professional HTML
                - Use proper heading syntax (<h1>, <h2>, <h3>)
                - Maintain consistent list formatting - use bullet lists when content naturally fits list format
                - Preserve table structures
                - Keep emphasis and formatting intact

                **INPUT SECTIONS** (combine in the order they appear, removing duplicates):
                {sectionsToCompine}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the assembled document in {targetLanguageName}
                - Format ONLY in HTML using strict HTML tags
                - MAINTAIN the page and section order as labeled
                - Remove ALL page/section labels ("PAGE X - SECTION Y")
                - **ELIMINATE ALL DUPLICATE CONTENT** - each sentence should appear only once unless it is explicitly mentioned in the context of the document
                - Do NOT include explanations, comments, or meta-text
                - Do NOT add phrases like "Combined document:", "Here is:", etc.
                - Start immediately with the document content
                - Create a single, flowing document that reads naturally from start to finish
                """;
    }

    private static string GenerateWordDocumentFinalPrompt(string targetLanguageName, string documentsToFinalize)
    {
        return $"""
                You are a professional document editor specializing in final document preparation.

                **FINAL ASSEMBLY TASK**: Create the definitive, publication-ready version of this {targetLanguageName} document.

                **DOCUMENT QUALITY STANDARDS**:
                1. **STRUCTURAL INTEGRITY**: Ensure the document has proper beginning, middle, and end
                2. **LOGICAL PROGRESSION**: Content should flow naturally from introduction to conclusion
                3. **CONSISTENCY**: Uniform terminology, formatting, and style throughout
                4. **COMPLETENESS**: All sections properly integrated without gaps or overlaps
                5. **PROFESSIONAL POLISH**: Ready for business or academic use

                **FINAL DEDUPLICATION CHECK**:
                - **MANDATORY**: Scan the entire document for any remaining duplicate content
                - Remove identical sentences, paragraphs, or content blocks
                - If similar content appears multiple times with slight variations, merge into single, best version
                - Ensure no repetitive content that disrupts document flow
                - Pay special attention to endings and transitions where duplicates commonly occur

                **EDITING GUIDELINES**:
                - Verify document flows logically from start to finish
                - Eliminate any remaining redundancies or awkward transitions
                - Ensure consistent heading hierarchy and numbering
                - Standardize formatting (lists, tables, emphasis)
                - Maintain technical accuracy and preserve all codes/references
                - Fix any grammatical issues or unclear passages
                - Ensure proper paragraph breaks and spacing

                **STRUCTURE VERIFICATION**:
                - Title/header should be at the beginning
                - Main content organized in logical sections
                - Conclusion or summary at the end (if present)
                - Consistent formatting throughout
                - No orphaned headings or broken lists
                - **NO DUPLICATE CONTENT ANYWHERE**

                **DOCUMENT TO FINALIZE**:
                {documentsToFinalize}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the final, polished document in {targetLanguageName}
                - Format ONLY in clean, publication-ready HTML using strict HTML tags
                - Ensure the document reads as a single, cohesive piece
                - **GUARANTEE NO DUPLICATE CONTENT** - every sentence must be unique
                - Do NOT include explanations, comments, or editing notes
                - Do NOT add phrases like "Final document:", "Here is:", "Edited version:", etc.
                - Start immediately with the document title/content
                - End immediately after the last line of document content
                - The result should be indistinguishable from a professionally prepared document
                """;
    }

        private static string GenerateImprovedTranslationPrompt(string translatedText, string targetLanguage, string feedback)
    {
        return $"""
                You are a translation system. Your task is to improve the translation.

                **Rules:**
                1. Address feedback points
                2. Keep technical terms and email addresses unchanged
                3. Maintain structure
                4. Format as HTML using strict HTML tags

                **Output:**
                - ONLY the improved text in {targetLanguage}
                - NO explanations or comments
                - NO prefixes like "Improved translation:" or "Here is:"
                - Start directly with the improved content

                **Original:**
                {translatedText}

                **Feedback:**
                {feedback}
                """;
    }

    private static string GenerateVerificationPrompt(string originalText, string targetLanguage, string improvedText)
    {
        return $"""
                <role>
                You are a translation system. Your task is to compare translations.
                </role>

                <rules>
                    1. Compare accuracy
                    2. Check flow
                    3. Verify technical terms
                    4. Assess formatting
                </rules>

                <output_format>
                    - Start with "A|" (original better) or "B|" (improved better)
                    - Follow with brief explanation
                    - NO other text
                </output_format>

                <original_text>
                    {originalText}
                </original_text>

                <improved_text>
                    {improvedText}
                </improved_text>
                """;
    } 

    private async Task<string> TranslateWordSectionWithRetry(string base64Data, int pageNumber, string sectionName, string targetLanguageName, AIModel model, CancellationToken cancellationToken, string extractedText = null, int maxRetries = 3)
    {
        Exception lastException = null;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var imageData = Convert.FromBase64String(base64Data);
                var translated = await ExtractAndTranslateWordDocumentWithClaude(imageData, pageNumber, sectionName, targetLanguageName, model, cancellationToken, extractedText);
                
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Successfully translated Word document page {PageNumber} section {SectionName} on attempt {Attempt}", 
                            pageNumber, sectionName, attempt);
                    }
                    return translated;
                }
                else
                {
                    _logger.LogWarning("Empty translation result for Word document page {PageNumber} section {SectionName} on attempt {Attempt}", 
                        pageNumber, sectionName, attempt);
                }
            }
            catch (Exception ex) when (IsTimeoutException(ex))
            {
                lastException = ex;
                _logger.LogWarning("Timeout on Word document translation attempt {Attempt}/{MaxRetries} for page {PageNumber} section {SectionName}: {ErrorMessage}", 
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
                _logger.LogWarning(ex, "Word document translation attempt {Attempt}/{MaxRetries} failed for page {PageNumber} section {SectionName}", 
                    attempt, maxRetries, pageNumber, sectionName);
                
                if (IsPermanentFailure(ex))
                {
                    _logger.LogError("Permanent failure detected for Word document page {PageNumber} section {SectionName}, stopping retries: {ErrorMessage}", 
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
        
        _logger.LogError(lastException, "All {MaxRetries} Word document translation attempts failed for page {PageNumber} section {SectionName}", 
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
}
