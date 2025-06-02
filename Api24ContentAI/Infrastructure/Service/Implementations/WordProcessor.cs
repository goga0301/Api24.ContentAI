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

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class WordProcessor(
    IClaudeService claudeService, 
    ILanguageService languageService, 
    IUserService userService, 
    IGptService gptService,
    ILogger<DocumentTranslationService> logger
) : IWordProcessor
{
    
    private readonly IClaudeService _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
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

    
    public async Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
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

            var translatedPages = new List<List<string>>();

            foreach (var page in screenshotResult.Pages)
            {
                if (page?.ScreenShots == null)
                {
                    _logger.LogWarning("Page {Page} has null screenshots, skipping", page?.Page ?? -1);
                    continue;
                }

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

                    try
                    {
                        var imageData = Convert.FromBase64String(base64Data);
                        var translated = await ExtractAndTranslateWordDocumentWithClaude(imageData, page.Page, sectionName, targetLanguage.Name, cancellationToken);
                        pageTranslations.Add(translated ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process page {Page} section {Section}", page.Page, i);
                        pageTranslations.Add(string.Empty);
                    }
                }

                translatedPages.Add(pageTranslations);
            }

            if (translatedPages.Count == 0 || translatedPages.All(p => p.All(string.IsNullOrWhiteSpace)))
                return new DocumentTranslationResult { Success = false, ErrorMessage = "No content could be extracted from the document" };

            var finalMarkdown = await CombineTranslatedWordSectionsWithClaude(translatedPages, targetLanguage.Name, cancellationToken);
            
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
            response = await httpClient.PostAsync("http://127.0.0.1:8000/screenshot", content, cancellationToken);
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
            _logger.LogError("Word document screenshot service returned null result");
            throw new InvalidOperationException("Word document screenshot service returned null");
        }

        _logger.LogInformation("Successfully received screenshots for {PageCount} pages", result.Pages?.Count ?? 0);
        return result;
    }

    private async Task<string> ExtractAndTranslateWordDocumentWithClaude(byte[] imageData, int pageNumber, string sectionName, string targetLanguageName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Extracting and translating text from Word document page {PageNumber} {SectionName} section with Claude", pageNumber, sectionName);
    
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning("Empty image data for page {PageNumber} {SectionName}", pageNumber, sectionName);
                return string.Empty;
            }

            string base64Image = Convert.ToBase64String(imageData);
    
            var messages = new List<ContentFile>
            {
                new ContentFile 
                { 
                    Type = "text", 
                    Text = ExtractWordDocumentTextAndTranslate(pageNumber, sectionName, targetLanguageName) 
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
    
            var claudeRequest = new ClaudeRequestWithFile(messages);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
    
            if (claudeResponse?.Content == null || !claudeResponse.Content.Any())
            {
                _logger.LogWarning("No content received from Claude for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }

            var content = claudeResponse.Content.FirstOrDefault();
            if (content?.Text == null)
            {
                _logger.LogWarning("Claude returned null text for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }
    
            string translatedText = content.Text.Trim();
    
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogWarning("Claude returned empty result for Word document page {PageNumber} {SectionName} section", pageNumber, sectionName);
                return string.Empty;
            }
    
            // Clean up common AI response prefixes
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

            var verifiedChunks = new List<string>();
            double totalQualityScore = 0.0;
            int successfulVerifications = 0;

            foreach (var chunk in translationChunks)
            {
                if (string.IsNullOrWhiteSpace(chunk.Value))
                {
                    verifiedChunks.Add(string.Empty);
                    continue;
                }

                try
                {
                    _logger.LogInformation("Verifying chunk {ChunkNumber} with {Length} characters", 
                        chunk.Key, chunk.Value.Length);

                    var verificationPrompt = GenerateGptVerificationPrompt(targetLanguage.Name, chunk.Value);
                    var verificationResult = await _gptService.EvaluateTranslationQuality(verificationPrompt, cancellationToken);
                    
                    if (verificationResult?.Success == true)
                    {
                        verifiedChunks.Add(verificationResult.Feedback ?? chunk.Value);
                        totalQualityScore += verificationResult.QualityScore ?? 0.5;
                        successfulVerifications++;
                        
                        _logger.LogInformation("GPT verification completed for chunk {ChunkNumber} with quality score: {Score}", 
                            chunk.Key, verificationResult.QualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("GPT verification failed for chunk {ChunkNumber}: {Error}", 
                            chunk.Key, verificationResult?.ErrorMessage ?? "Unknown error");
                        
                        var claudeVerifiedText = await VerifyWithClaude(chunk.Value, targetLanguage.Name, cancellationToken);
                        
                        if (!string.IsNullOrWhiteSpace(claudeVerifiedText))
                        {
                            verifiedChunks.Add(claudeVerifiedText);
                            totalQualityScore += 0.7;
                            successfulVerifications++;
                            _logger.LogInformation("Claude fallback verification completed for chunk {ChunkNumber}", chunk.Key);
                        }
                        else
                        {
                            _logger.LogWarning("Both GPT and Claude verification failed for chunk {ChunkNumber}, using original", chunk.Key);
                            verifiedChunks.Add(chunk.Value);
                            totalQualityScore += 0.5;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error verifying chunk {ChunkNumber}", chunk.Key);
                    verifiedChunks.Add(chunk.Value);
                    totalQualityScore += 0.5;
                }
            }

            var averageQualityScore = translationChunks.Count > 0 ? totalQualityScore / translationChunks.Count : 0.0;
            var verifiedTranslation = string.Join("\n\n", verifiedChunks.Where(c => !string.IsNullOrWhiteSpace(c)));

            var result = new TranslationVerificationResult
            {
                Success = successfulVerifications > 0,
                QualityScore = averageQualityScore,
                ErrorMessage = successfulVerifications == 0 ? "No chunks could be verified" : null
            };

            _logger.LogInformation("Translation verification completed. Success rate: {Rate}%, Average quality: {Quality}", 
                (successfulVerifications * 100.0) / translationChunks.Count, averageQualityScore);

            return (result, verifiedTranslation);
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

    private async Task<string> VerifyWithClaude(string text, string targetLanguageName, CancellationToken cancellationToken)
    {
        try
        {
            var claudeVerificationPrompt = GenerateVerificationPrompt(targetLanguageName, text);
            
            var message = new ContentFile { Type = "text", Text = claudeVerificationPrompt };
            var claudeRequest = new ClaudeRequestWithFile([message]);
            
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            var content = claudeResponse?.Content?.FirstOrDefault();
            
            if (content?.Text != null)
            {
                var verifiedText = CleanAiResponsePrefix(content.Text.Trim());
                return string.IsNullOrWhiteSpace(verifiedText) ? text : verifiedText;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Claude verification fallback");
        }
        
        return text;
    }

    private async Task<string> CombineTranslatedWordSectionsWithClaude(List<List<string>> translatedPageSections, string targetLanguageName, CancellationToken cancellationToken)
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
                var combinedChunk = await ProcessChunkCombination(chunks[i], targetLanguageName, i + 1, chunks.Count, cancellationToken);
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

    private async Task<string> ProcessChunkCombination(List<string> chunk, string targetLanguageName, int chunkIndex, int totalChunks, CancellationToken cancellationToken)
    {
        try
        {
            var chunkContent = string.Join("\n\n===== SECTION SEPARATOR =====\n\n", chunk);
    
            _logger.LogInformation("Processing Word document chunk {ChunkIndex}/{ChunkCount} with {SectionCount} sections, size: {Size} characters", 
                chunkIndex, totalChunks, chunk.Count, chunkContent.Length);
    
            var prompt = GenerateWordDocumentCombinationPrompt(targetLanguageName, chunkContent);
    
            var message = new ContentFile { Type = "text", Text = prompt };
            var claudeRequest = new ClaudeRequestWithFile([message]);
    
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
    
            var content = claudeResponse?.Content?.FirstOrDefault();
            if (content?.Text == null)
            {
                _logger.LogWarning("No content received from Claude for combining Word document chunk {ChunkIndex}", chunkIndex);
                return string.Join("\n\n", chunk);
            }
    
            string combinedChunk = CleanAiResponsePrefix(content.Text.Trim());
            
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
    
            var finalMessage = new ContentFile { Type = "text", Text = finalPrompt };
            var finalRequest = new ClaudeRequestWithFile([finalMessage]);
    
            var finalResponse = await _claudeService.SendRequestWithFile(finalRequest, cancellationToken);
    
            var finalContent = finalResponse?.Content?.FirstOrDefault();
            if (finalContent?.Text != null)
            {
                string finalResult = CleanAiResponsePrefix(finalContent.Text.Trim());
        
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

        var prefixes = new[]
        {
            "Here", "I've translated", "The translated", "I've combined", "Here's the",
            "I've", "Here is", "The following", "Below is"
        };

        foreach (var prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            int firstLineBreak = text.IndexOf('\n');
            if (firstLineBreak > 0)
            {
                return text.Substring(firstLineBreak + 1).Trim();
            }
        }

        return text;
    }
    
    private List<string> GetChunksOfText(string text, int maxChunkSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (text.Length <= maxChunkSize)
            return [text];

        var chunks = new List<string>();
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
    
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

                **Document Context:**
                -   Page Number: {pageNumber}
                -   Section of Page: {sectionName} (representing approximately the {verticalPortion} vertical portion of the page)
                -   Document Type: Microsoft Word Document (.docx or .doc)

                **Required Actions:**
                1.  **Extract Text**: Accurately extract ALL visible textual content from the provided Word document image section, including:
                * Headers and footers if visible
                * Body text and paragraphs
                * Bullet points and numbered lists
                * Table content if present
                * Any captions or annotations
                2.  **Translate**: Translate the entire extracted text into **{targetLanguageName}**.
                3.  **Format as Markdown**: Present the translated content in well-structured Markdown:
                * Headings: Use `#` syntax (e.g., `# Main Heading`, `## Subheading`).
                * Lists: Use `-` or `*` for bullet points, or numbered lists (e.g., `1. Item`).
                * Emphasis: Use `**bold**` for strong emphasis and `*italic*` for regular emphasis.
                * Tables: If tabular data is present, format it using Markdown table syntax.
                * Separators: Use horizontal rules (`---`) to logically separate distinct content sections where appropriate.
                * Code/Technical Blocks: Format code snippets or highly structured technical content using triple backticks (```).
                4.  **Preserve Original Data**:
                * Keep all numbers, dates, and specific codes exactly as they appear in the original, or transliterate them appropriately if they are part of a sentence structure that requires it in {targetLanguageName}.
                * Maintain any special formatting cues visible in the Word document (indentation, spacing).
                5.  **CRITICAL - Non-Translation Rules**:
                * **DO NOT TRANSLATE** the following items:
                * Technical identifiers, model numbers, part numbers.
                * Standards (e.g., ISO, EN, ÃÃÃÃÃ, ÃÃÃÃÃ).
                * Specific codes (e.g., ÃÃÂ¤Ã ÃÃÃÂ£, ÃÃÃÃÂ£).
                * Reference numbers.
                * These items must be preserved in their original form.
                6.  **Proper Nouns**: Transliterate proper nouns (names of people, organizations, specific places) according to standard {targetLanguageName} conventions if a common translation doesn't exist.
                7.  **Word Document Structure**: Pay special attention to Word-specific elements:
                * Document titles and subtitles
                * Section breaks and page breaks
                * Footnotes or endnotes if visible
                * Any tracked changes or comments (if visible)

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Your response must contain ONLY the translated text in {targetLanguageName}
                - Format ONLY in Markdown
                - Do NOT include ANY explanations, comments, reasoning, or meta-text
                - Do NOT include phrases like "Here is the translation:", "Translation:", or similar
                - Do NOT include any portion of the original untranslated text
                - Do NOT add introductory or concluding statements
                - Start your response directly with the translated content
                - End your response immediately after the translated content
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
                5. Keep all codes, standards, and reference numbers unchanged
                6. Maintain professional document tone

                **Translation to review**:

                {translatedText}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the improved translation in {targetLanguage}
                - Format ONLY in Markdown
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
                5. Keep all codes, standards, and reference numbers unchanged
                6. Maintain professional document tone

                **Translation to review**:

                {translatedText}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the improved translation in {targetLanguage}
                - Format ONLY in Markdown
                - Do NOT include any explanations, comments, or reasoning
                - Do NOT include phrases like "Improved translation:", "Here is:", etc.
                - Start your response directly with the improved translated content
                """;
    }

    private static string GenerateWordDocumentCombinationPrompt(string targetLanguage, string sectionsToCompine)
    {
        return $"""
                You are a document assembly specialist working with translated Word document sections.

                **Task**: Combine the following translated sections into a cohesive {targetLanguage} document.

                **Assembly Guidelines**:
                1. Merge sections logically, removing redundant section headers
                2. Ensure smooth transitions between sections
                3. Maintain document hierarchy and structure
                4. Preserve all technical information, codes, and formatting
                5. Create a natural flow while keeping all content
                6. Format as clean, professional Markdown
                7. Remove section separators and page/section labels

                **Sections to combine**:

                {sectionsToCompine}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the combined document in {targetLanguage}
                - Format ONLY in Markdown
                - Do NOT include explanations, comments, or reasoning
                - Do NOT include phrases like "Combined document:", "Here is:", etc.
                - Start your response directly with the combined document content
                """;
    }

    private static string GenerateWordDocumentFinalPrompt(string targetLanguage, string documentsToFinalize)
    {
        return $"""
                You are a document finalization specialist for translated Word documents.

                **Task**: Create the final, polished version of this {targetLanguage} document.

                **Finalization Guidelines**:
                1. Ensure the document flows naturally from beginning to end
                2. Remove any remaining redundancies or awkward transitions
                3. Maintain professional document structure
                4. Preserve all technical information and formatting
                5. Ensure consistent terminology throughout
                6. Format as clean, publication-ready Markdown
                7. Maintain the logical order and hierarchy of content

                **Document to finalize**:

                {documentsToFinalize}

                **CRITICAL OUTPUT REQUIREMENTS:**
                - Provide ONLY the final, polished document in {targetLanguage}
                - Format ONLY in Markdown
                - Do NOT include explanations, comments, or reasoning
                - Do NOT include phrases like "Final document:", "Here is:", etc.
                - Start your response directly with the finalized document content
                - End your response immediately after the document content
                """;
    } 
}