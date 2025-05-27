using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationService(
        IClaudeService claudeService,
        ILanguageService languageService,
        IUserRepository userRepository,
        IGptService gptService,
        ILogger<DocumentTranslationService> logger)
        : IDocumentTranslationService
    {
        private readonly IClaudeService _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
        private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        private readonly IUserRepository _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        private readonly IGptService _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
        private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));


        public async Task<DocumentTranslationResult> TranslateDocumentWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
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

                var user = await _userRepository.GetById(userId, cancellationToken);
                if (user == null)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User not found" };
                }

                var ocrTxtContent = await SendFileToOcrService(file, cancellationToken);
                
                var translationResult = await TranslateOcrContent(ocrTxtContent, targetLanguageId, userId, cancellationToken);
                
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
        
        public async Task<DocumentTranslationResult> TranslateDocumentWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
        {
            if (file?.Length == 0)
                throw new ArgumentException("Uploaded file is empty or null", nameof(file));

            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);

            if (targetLanguage == null || string.IsNullOrWhiteSpace(targetLanguage.Name))
                throw new ArgumentException("Invalid target language ID", nameof(targetLanguageId));

            var screenshotResult = await GetDocumentScreenshots(file, cancellationToken);
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

                    var imageData = Convert.FromBase64String(base64Data);

                    var translated = await ExtractAndTranslateWithClaude(imageData, page.Page, sectionName, targetLanguage.Name, cancellationToken);
                    pageTranslations.Add(translated);
                }

                translatedPages.Add(pageTranslations);
            }

            var finalMarkdown = await CombineTranslatedSectionsWithClaude(translatedPages, targetLanguage.Name, cancellationToken);
            
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

                     var (verificationResult, verifiedTranslation) = await ProcessTranslationVerification(
                         cancellationToken, translationChunksForVerification, targetLanguage, finalMarkdown);
            
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

        public async Task<DocumentTranslationResult> TranslateSRTFiles(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
            CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "No file provided"};
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "User ID is required"};
            }
            
            var user = await _userRepository.GetById(userId, cancellationToken);
            if(user == null)
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "User not found"};
            }
            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);
            if (targetLanguage == null || string.IsNullOrWhiteSpace(targetLanguage.Name))
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "Invalid target language ID"};
            }
            
            _logger.LogInformation("Processing SRT file {FileName} with target language {TargetLanguageName}", file.FileName, targetLanguage.Name);
            string srtContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                srtContent = await reader.ReadToEndAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(srtContent))
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "File is empty"};
            }
            
            var subtitleEntries = ParseSrtContent(srtContent);
            if (subtitleEntries.Count == 0)
            {
                return new DocumentTranslationResult {Success = false, ErrorMessage = "File contains no subtitle entries"};
            }
            _logger.LogInformation("Parsed {Count} subtitle entries from SRT file", subtitleEntries.Count);
            
            var textsToTranslate = subtitleEntries.Select(entry => entry.Text).ToList();
            var combinedText = string.Join("\n\n", textsToTranslate);
            
            var translatedText = await TranslateSrtContent(combinedText, targetLanguage, cancellationToken);
        
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation failed" };
            }

            var translatedLines = translatedText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        
            var translatedSrtContent = RebuildSrtFile(subtitleEntries, translatedLines);

            string improvedTranslation = translatedSrtContent;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedSrtContent))
            {
                _logger.LogInformation("Starting SRT translation verification");
            
                try
                {
                    var verificationText = string.Join("\n", translatedLines);
                
                    var verificationResult = await _gptService.EvaluateTranslationQuality(
                        $"Evaluate this SRT subtitle translation to {targetLanguage.Name}. Focus on subtitle-appropriate language, timing considerations, and readability:\n\n{verificationText}", 
                        cancellationToken);
                
                    if (verificationResult.Success)
                    {
                        qualityScore = verificationResult.QualityScore ?? 1.0;
                        _logger.LogInformation("SRT translation verification completed with score: {Score}", qualityScore);
                    
                        // If quality is low, try to improve the translation
                        if (qualityScore < 0.8 && !string.IsNullOrEmpty(verificationResult.Feedback))
                        {
                            _logger.LogInformation("Attempting to improve SRT translation based on feedback");
                            var improvedText = await ImproveSrtTranslation(combinedText, targetLanguage.Name, verificationResult.Feedback, cancellationToken);
                        
                            if (!string.IsNullOrEmpty(improvedText))
                            {
                                var improvedLines = improvedText.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                                improvedTranslation = RebuildSrtFile(subtitleEntries, improvedLines);
                            
                                // Re-verify improved translation
                                var improvedVerification = await _gptService.EvaluateTranslationQuality(
                                    $"Evaluate this improved SRT subtitle translation to {targetLanguage.Name}:\n\n{improvedText}", 
                                    cancellationToken);
                            
                                if (improvedVerification.Success && improvedVerification.QualityScore > qualityScore)
                                {
                                    qualityScore = improvedVerification.QualityScore ?? qualityScore;
                                    _logger.LogInformation("Improved SRT translation score: {Score}", qualityScore);
                                }
                                else
                                {
                                    // Keep original if improvement didn't work
                                    improvedTranslation = translatedSrtContent;
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("SRT translation verification failed: {Error}", verificationResult.ErrorMessage);
                        qualityScore = 0.7; // Default score
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during SRT translation verification");
                    qualityScore = 0.7; // Default score
                }
            }

            string translationId = Guid.NewGuid().ToString();
        
            var result = new DocumentTranslationResult
            {
                Success = true,
                OriginalContent = srtContent,
                TranslatedContent = improvedTranslation,
                OutputFormat = Domain.Models.DocumentFormat.Srt,
                FileData = Encoding.UTF8.GetBytes(improvedTranslation),
                FileName = $"translated_{translationId}.srt",
                ContentType = "text/plain",
                TranslationQualityScore = qualityScore,
                TranslationId = translationId
            };

            _logger.LogInformation("SRT translation completed successfully. Quality score: {Score}", qualityScore);
            return result;
        }
        
        private List<SrtEntry> ParseSrtContent(string srtContent)
        {
            var entries = new List<SrtEntry>();
            var lines = srtContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    
            for (int i = 0; i < lines.Length; i++)
            {
                // Look for sequence number
                if (int.TryParse(lines[i].Trim(), out int sequenceNumber))
                {
                    // Next line should be timestamp
                    if (i + 1 < lines.Length && lines[i + 1].Contains("-->"))
                    {
                        var timestamp = lines[i + 1].Trim();
                
                        // Collect subtitle text until next sequence number or end
                        var textLines = new List<string>();
                        int textIndex = i + 2;
                
                        while (textIndex < lines.Length && 
                               !int.TryParse(lines[textIndex].Trim(), out _))
                        {
                            textLines.Add(lines[textIndex]);
                            textIndex++;
                        }
                
                        if (textLines.Count > 0)
                        {
                            entries.Add(new SrtEntry
                            {
                                SequenceNumber = sequenceNumber,
                                Timestamp = timestamp,
                                Text = string.Join("\n", textLines).Trim()
                            });
                        }
                
                        i = textIndex - 1; // Skip processed lines
                    }
                }
            }
    
            return entries;
        }
        
        private async Task<string> TranslateSrtContent(string combinedText, LanguageModel targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                var prompt = GenerateSrtTranslationPrompt(targetLanguage.Name, combinedText);
        
                var message = new ContentFile { Type = "text", Text = prompt };
                var claudeRequest = new ClaudeRequestWithFile([message]);
        
                _logger.LogInformation("Sending SRT content to Claude for translation");
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude for SRT translation");
                    return string.Empty;
                }
        
                string translatedText = content.Text.Trim();
        
                // Clean up Claude's response if it includes explanatory text
                if (translatedText.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                    translatedText.StartsWith("I've translated", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = translatedText.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        translatedText = translatedText.Substring(firstLineBreak + 1).Trim();
                    }
                }
        
                _logger.LogInformation("Successfully translated SRT content, length: {Length} characters", translatedText.Length);
                return translatedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating SRT content with Claude");
                return string.Empty;
            }
        }
        
        private async Task<string> ImproveSrtTranslation(string originalText, string targetLanguage, string feedback, CancellationToken cancellationToken)
        {
            try
            {
                var prompt = GenerateSrtImprovementPrompt(originalText, targetLanguage, feedback);
        
                var message = new ContentFile { Type = "text", Text = prompt };
                var claudeRequest = new ClaudeRequestWithFile([message]);
        
                _logger.LogInformation("Sending SRT improvement request to Claude");
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude for SRT improvement");
                    return string.Empty;
                }
        
                string improvedText = content.Text.Trim();
        
                if (improvedText.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                    improvedText.StartsWith("Improved", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = improvedText.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        improvedText = improvedText.Substring(firstLineBreak + 1).Trim();
                    }
                }
        
                return improvedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error improving SRT translation");
                return string.Empty;
            }
        }
        
        private string RebuildSrtFile(List<SrtEntry> entries, string[] translatedLines)
        {
            var result = new StringBuilder();
    
            for (int i = 0; i < entries.Count && i < translatedLines.Length; i++)
            {
                result.AppendLine(entries[i].SequenceNumber.ToString());
                result.AppendLine(entries[i].Timestamp);
                result.AppendLine(translatedLines[i].Trim());
                result.AppendLine(); // Empty line between entries
            }
    
            return result.ToString().TrimEnd();
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

        
        private async Task<string> ExtractAndTranslateWithClaude(byte[] imageData, int pageNumber, string sectionName, string targetLanguageName, CancellationToken cancellationToken)
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
        
                var claudeRequest = new ClaudeRequestWithFile(messages);
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude for page {PageNumber} {SectionName} section", pageNumber, sectionName);
                    return string.Empty;
                }
        
                string translatedText = content.Text.Trim();
        
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
        private async Task<string> CombineTranslatedSectionsWithClaude(List<List<string>> translatedPageSections, string targetLanguageName, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Combining translated sections from {PageCount} pages using Claude", translatedPageSections.Count);
        
                var allSections = new List<string>();
                for (int i = 0; i < translatedPageSections.Count; i++)
                {
                    var pageSections = translatedPageSections[i];
                    allSections.AddRange(pageSections.Select((section, idx) => 
                        $"PAGE {i + 1} - SECTION {idx + 1} ({(idx == 0 ? "TOP" : (idx == 1 ? "MIDDLE" : "BOTTOM"))}):\n\n{section}"));
                }
        
                var chunks = new List<List<string>>();
                var currentChunk = new List<string>();
                int currentLength = 0;
                const int maxChunkLength = 80000;
        
                foreach (var section in allSections)
                {
                    if (currentLength + section.Length > maxChunkLength)
                    {
                        chunks.Add([..currentChunk]);
                        currentChunk.Clear();
                        currentLength = 0;
                    }
            
                    currentChunk.Add(section);
                    currentLength += section.Length;
                }
        
                if (currentChunk.Count > 0)
                {
                    chunks.Add(currentChunk);
                }
        
                _logger.LogInformation("Split sections into {ChunkCount} chunks for combination", chunks.Count);
        
                var combinedChunks = new List<string>();
        
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    var chunkContent = string.Join("\n\n===== SECTION SEPARATOR =====\n\n", chunk);
            
                    _logger.LogInformation("Processing chunk {ChunkIndex}/{ChunkCount} with {SectionCount} sections, size: {Size} characters", 
                        i + 1, chunks.Count, chunk.Count, chunkContent.Length);
            
                    var prompt = GenerateDocumentCombinationPrompt(targetLanguageName, chunkContent);
            
                    var message = new ContentFile { Type = "text", Text = prompt };
                    var claudeRequest = new ClaudeRequestWithFile([message]);
            
                    var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
                    var content = claudeResponse.Content?.SingleOrDefault();
                    if (content == null)
                    {
                        _logger.LogWarning("No content received from Claude for combining chunk {ChunkIndex}", i + 1);
                        continue;
                    }
            
                    string combinedChunk = content.Text.Trim();
                    if (string.IsNullOrWhiteSpace(combinedChunk))
                    {
                        _logger.LogWarning("Claude returned empty result for combining chunk {ChunkIndex}", i + 1);
                        continue;
                    }
            
                    if (combinedChunk.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                        combinedChunk.StartsWith("I've combined", StringComparison.OrdinalIgnoreCase) ||
                        combinedChunk.StartsWith("Here's the", StringComparison.OrdinalIgnoreCase))
                    {
                        int firstLineBreak = combinedChunk.IndexOf('\n');
                        if (firstLineBreak > 0)
                        {
                            combinedChunk = combinedChunk.Substring(firstLineBreak + 1).Trim();
                        }
                    }
            
                    _logger.LogInformation("Successfully combined chunk {ChunkIndex}, result length: {Length} characters", 
                        i + 1, combinedChunk.Length);
            
                    combinedChunks.Add(combinedChunk);
                }
        
                if (combinedChunks.Count > 1)
                {
                    _logger.LogInformation("Combining {Count} final chunks", combinedChunks.Count);
            
                    var finalCombination = string.Join("\n\n", combinedChunks);
            
                    var finalPrompt = GenerateFinalPrompt(targetLanguageName, finalCombination);
            
                    var finalMessage = new ContentFile { Type = "text", Text = finalPrompt };
                    var finalRequest = new ClaudeRequestWithFile([finalMessage]);
            
                    var finalResponse = await _claudeService.SendRequestWithFile(finalRequest, cancellationToken);
            
                    var finalContent = finalResponse.Content?.SingleOrDefault();
                    if (finalContent != null)
                    {
                        string finalResult = finalContent.Text.Trim();
                
                        if (finalResult.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                            finalResult.StartsWith("I've combined", StringComparison.OrdinalIgnoreCase))
                        {
                            int firstLineBreak = finalResult.IndexOf('\n');
                            if (firstLineBreak > 0)
                            {
                                finalResult = finalResult.Substring(firstLineBreak + 1).Trim();
                            }
                        }
                
                        _logger.LogInformation("Successfully combined all chunks into final document, length: {Length} characters", finalResult.Length);
                
                        return finalResult;
                    }
            
                    _logger.LogWarning("Failed to combine final chunks, returning concatenated chunks");
                    return finalCombination;
                }
        
                return combinedChunks.Count > 0 ? combinedChunks[0] : string.Empty;
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

        private async Task<DocumentTranslationResult> TranslateOcrContent(string ocrTxtContent, int targetLanguageId, string userId, CancellationToken cancellationToken)
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
                        var claudeRequest = new ClaudeRequestWithFile([message]);
                        
                        _logger.LogInformation("Sending chunk {Index}/{Total} to Claude for translation", 
                            i + 1, chunks.Count);
                        
                        var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                        
                        var content = claudeResponse.Content?.SingleOrDefault();
                        if (content == null)
                        {
                            _logger.LogWarning("No content received from Claude service for chunk {Index}/{Total}", 
                                i + 1, chunks.Count);
                            continue;
                        }
                        
                        string translatedChunk = content.Text.Trim();
                        if (string.IsNullOrWhiteSpace(translatedChunk))
                        {
                            _logger.LogWarning("Claude returned empty translation result for chunk {Index}/{Total}", 
                                i + 1, chunks.Count);
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
                (var verificationResult, translatedText) = await ProcessTranslationVerification(cancellationToken, translationChunksForVerification, language, translatedText);

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
        
        private async Task<(VerificationResult verificationResult, string translatedText)> ProcessTranslationVerification(CancellationToken cancellationToken,
            List<KeyValuePair<int, string>> translationChunksForVerification, LanguageModel language, string translatedText)
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
                        translatedText, language.Name, verificationResult.Feedback, cancellationToken);
                        
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
                    string[] sentences = Regex.Split(paragraph, @"(?<=[.!?])\s+");
                    
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


        private async Task<string> ImproveTranslationWithFeedback(string translatedText, string targetLanguage, string feedback, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Attempting to improve translation based on GPT feedback");
                
                var prompt = GenerateImprovedTranslationPrompt(translatedText, targetLanguage, feedback);
                
                var message = new ContentFile { Type = "text", Text = prompt };
                var claudeRequest = new ClaudeRequestWithFile([message]);
                
                _logger.LogInformation("Sending improvement request to Claude");
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude service for translation improvement");
                    return string.Empty;
                }
                
                string improvedTranslation = content.Text.Trim();
                
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

        private static string GenerateVerificationPrompt(string translatedText, string targetLanguage, string improvedTranslation)
        {
            return $@"
                        Compare these two translations to {targetLanguage} and determine which one is better quality:

                        TRANSLATION A:
                        {translatedText}

                        TRANSLATION B:
                        {improvedTranslation}

                        Evaluate based on:
                        1. Fluency and naturalness of language
                        2. Consistency of terminology
                        3. Absence of untranslated text
                        4. Overall quality

                        Respond with either 'A' or 'B' to indicate which translation is better, followed by a brief explanation.
                        Format: <A or B>|<explanation>
                    ";
        }

        private static string GenerateImprovedTranslationPrompt(string translatedText, string targetLanguage, string feedback)
        {
            return $"""
                          You are a professional translator specializing in {targetLanguage}. I have a text that has been translated to {targetLanguage}, 
                          but there are some issues with the translation that need to be fixed.

                          Here is the feedback from a quality review:
                          {feedback}

                          Please improve the translation by addressing these issues. Focus specifically on:
                          1. Correcting any untranslated words or phrases that should have been translated
                          2. Ensuring consistency in terminology throughout the document
                          3. Fixing any awkward phrasing to make the text flow naturally in {targetLanguage}
                          4. Maintaining proper formatting and structure
                          5. Ensuring technical terms are translated correctly using standard {targetLanguage} equivalents
                          6. Preserving technical identifiers, standards (like ISO, EN), codes, and reference numbers in their original form


                          Return the complete improved translation with all issues fixed.
                          DO NOT INCLUDE ANY INTRODUCTORY TEXT OR EXPLANATIONS.

                          Here is the current translation:
                          {translatedText}
                              
                          """;
        }

        private static string ExtractTextAndTranslate(int pageNumber, string sectionName, string targetLanguageName)
        {
            return $"""
                    You are an advanced OCR and translation system that can extract text from images, recognize document structure, and translate content.

                    This is the {sectionName} section (representing {(sectionName == "top" ? "0-50%" : (sectionName == "middle" ? "25-75%" : "50-100%"))} vertical portion) of page {pageNumber} of a document.

                    I need you to:
                    1. Extract ALL visible text from the image
                    2. Translate all the extracted text into {targetLanguageName}
                    3. Format the translated content as proper Markdown:
                       - Identify and format headings using # syntax (# for main headings, ## for subheadings, etc.)
                       - Format lists with proper bullet points or numbers
                       - Use **bold** and *italic* for emphasized text
                       - Create proper tables if table-like data is present
                       - Add horizontal rules (---) where appropriate to separate sections
                       - Format code blocks or technical content with ```
                    4. Keep all numbers, dates, codes, and technical identifiers exactly as they appear in the original
                    5. IMPORTANT: Do NOT translate technical identifiers, standards (like ISO, EN, ÐÐ¡Ð¢Ð£, etc.), codes (like ÐÐÐ ÐÐÐ£, ÐÐÐÐ£), and reference numbers
                    6. For proper nouns, transliterate according to {targetLanguageName} conventions if appropriate
                    7. DO NOT include any explanations or remarks about the translation process
                    8. DO NOT include the original text alongside your translation
                    9. Provide ONLY the translated text in {targetLanguageName}, formatted in Markdown
                    10. IMPORTANT: Use contextual clues to determine document structure - identify titles, section headings, lists, etc.

                    Respond ONLY with the translated text in Markdown format.
                    """;
        }
        
        private static string GenerateTranslationPrompt(LanguageModel language, int i, List<string> chunks, string chunk)
        {
            var prompt = $"""
                          You are a professional translator. I will send you OCR-extracted content from a PDF document.

                          Your task is to:
                          1. Translate **all text** into **{language.Name}**.
                          2. **Preserve the original formatting and layout** of the text as closely as possible.
                          3. Keep **all numbers, dates, codes, and identifiers** exactly as they appear in the source.
                          4. **Fix or ignore OCR artifacts** (e.g., strange characters), using context to infer the correct form.
                          5. Maintain **paragraph breaks and line spacing**.
                          6. Return the translated text with the **same structure and order** as the original.
                          7. **Do NOT translate** any of the following: technical identifiers, standards (e.g., ISO, EN, ÐÐ¡Ð¢Ð£), codes (e.g., ÐÐÐ ÐÐÐ£, ÐÐÐÐ£), reference numbers.
                          8. For **proper nouns**, **transliterate** them according to {language.Name} norms, where appropriate.
                          9. Use **standard technical terms** in {language.Name} for accuracy.
                          10. **Do NOT include** English explanations, remarks, or original untranslated text.
                          11. **Do NOT add** any notes, comments, or clarifications.
                          12. Return **only the final translated text** in {language.Name}, with no extra commentary.
                          13. If you see **duplicate text**, **do not remove it**âtranslate everything exactly as-is.
                          14. Ensure **no content is skipped** translate every word and leave nothing untranslated.
                          15. **Format the output using Markdown** (for use in README files or similar). Use:
                               - `#`, `##`, etc. for headings
                               - `-` or `*` for bullet points
                               - Code blocks (triple backticks) for sections that are structured like forms or certificates
                               - Maintain line breaks and paragraph spacing
                               - use table formatting when you encounter table-like data
                               - use horizontal rules (---) to separate sections
                          16. **Do NOT include** any introductory text or explanations.
                          17. *Do NOT include* unique characters or text which is out of context or some characters which are readable

                          **Note**: If the context indicates that the output will be used in a `README.md` or similar file, format your output using Markdown accordingly (e.g., for headings, lists, or code blocks).

                          Here is the OCR-extracted text (chunk {i + 1} of {chunks.Count}):

                          {chunk}
                          """;
            return prompt;
        }

        private static string GenerateFinalPrompt(string targetLanguageName, string finalCombination)
        {
            return $"""
                       You are a document formatting expert. I have a document that was processed in multiple chunks and now needs to be combined into a single cohesive document.

                       Your task:
                       1. Combine these chunks into a single cohesive document in {targetLanguageName}
                       2. Remove overlap between chunks
                       3. Ensure that the document flows naturally and maintains proper structure
                       4. Preserve all Markdown formatting (headings, lists, tables, etc.)
                       5. DO NOT add any new content or remark
                       6. Return ONLY the final combined document in Markdown format
                       7. **Based on the context of the text add formatting such as headers '#' or other markdown elements to improve readability **

                       Here are the chunks to combine:

                       {finalCombination}
                    """;
        }
        
        private static string GenerateDocumentCombinationPrompt(string targetLanguageName, string chunkContent)
        {
            var prompt = $"""
                          You are a document formatting expert. I've extracted and translated different sections of a document, but they are overlapping and need to be combined into a single coherent document.

                          I'll provide you with several sections from the document, with markers indicating which page and part of the page (top, middle, bottom) they came from.

                          Your task:
                          1. Combine all these sections into a single cohesive document in {targetLanguageName}
                          2. Remove any duplicate content from overlapping sections
                          3. Maintain the proper order of content (using page and section numbers as a guide)
                          4. Preserve the document structure (headings, paragraphs, lists, etc.)
                          5. Ensure that the document flows naturally
                          6. Maintain all Markdown formatting (headings, lists, tables, etc.)
                          7. DO NOT add any content that wasn't in the original sections
                          8. DO NOT include the page and section markers in your final output
                          9. DO NOT include any explanations or notes about what you did
                          10. Return ONLY the final combined document in Markdown format

                          Here are the sections to combine:

                          {chunkContent}
                          """;
            return prompt;
        }
        
        private static string GenerateSrtTranslationPrompt(string targetLanguage, string combinedText)
        {
            return $"""
                    You are a professional subtitle translator specializing in {targetLanguage}. I will provide you with subtitle text from an SRT file that needs to be translated.

                    Your task is to:
                    1. Translate ALL subtitle text into {targetLanguage}
                    2. Keep subtitles concise and readable (suitable for on-screen display)
                    3. Maintain the same number of subtitle entries as the original
                    4. Use natural, conversational language appropriate for subtitles
                    5. Preserve emphasis and emotional tone where possible
                    6. Keep line breaks within individual subtitles if they exist
                    7. DO NOT translate proper names unless they have standard translations
                    8. DO NOT include any explanations or notes
                    9. Return ONLY the translated subtitle text, separated by double line breaks (\\n\\n)
                    10. Ensure each subtitle is concise enough to be read quickly on screen

                    Here are the subtitle texts to translate (separated by double line breaks):

                    {combinedText}
                    """;
        }

        private static string GenerateSrtImprovementPrompt(string originalText, string targetLanguage, string feedback)
        {
            return $"""
                    You are a professional subtitle translator. I have subtitle text that has been translated to {targetLanguage}, but there are quality issues that need to be addressed.

                    Feedback from quality review:
                    {feedback}

                    Please improve the translation by addressing these issues while keeping in mind subtitle-specific requirements:
                    1. Keep subtitles concise and readable on screen
                    2. Use natural, conversational language
                    3. Maintain proper timing considerations (subtitles should be quick to read)
                    4. Fix any awkward phrasing or unnatural language
                    5. Ensure consistency in terminology and style
                    6. Preserve the emotional tone and context
                    7. Keep the same number of subtitle entries

                    Return ONLY the improved translated subtitle text, separated by double line breaks (\\n\\n).
                    DO NOT include any explanations or introductory text.

                    Current translation:
                    {originalText}
                    """;
        }
        
    }
}
