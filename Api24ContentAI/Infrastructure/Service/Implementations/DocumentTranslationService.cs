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

                var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);
                if (targetLanguage == null)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language not found" };
                }

                _logger.LogInformation("Processing SRT file {FileName} for translation to {LanguageName}", 
                    file.FileName, targetLanguage.Name);

                string srtContent;
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    srtContent = await reader.ReadToEndAsync(cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(srtContent))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "SRT file is empty" };
                }

                var subtitleEntries = ParseSrtContent(srtContent);
                if (subtitleEntries.Count == 0)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No valid subtitle entries found in SRT file" };
                }

                _logger.LogInformation("Parsed {Count} subtitle entries from SRT file", subtitleEntries.Count);

                var textsToTranslate = subtitleEntries.Select(entry => entry.Text).ToList();
        
                var translatedTexts = await TranslateSrtContentInChunks(textsToTranslate, targetLanguage, cancellationToken);
        
                if (translatedTexts == null || translatedTexts.Count != textsToTranslate.Count)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation failed or mismatch in subtitle count" };
                }

                var translatedSrtContent = RebuildSrtFileWithTranslatedTexts(subtitleEntries, translatedTexts);

                string improvedTranslation = translatedSrtContent;
                double qualityScore = 0.0;

                if (!string.IsNullOrWhiteSpace(translatedSrtContent))
                {
                    _logger.LogInformation("Starting SRT translation verification");
            
                    try
                    {
                        var sampleTexts = translatedTexts.Take(Math.Min(10, translatedTexts.Count)).ToList();
                        var verificationText = string.Join("\n", sampleTexts);
                
                        var verificationResult = await _gptService.EvaluateTranslationQuality(
                            $"Evaluate this SRT subtitle translation sample to {targetLanguage.Name}. Focus on subtitle-appropriate language, timing considerations, and readability:\n\n{verificationText}", 
                            cancellationToken);
                
                        if (verificationResult.Success)
                        {
                            qualityScore = verificationResult.QualityScore ?? 1.0;
                            _logger.LogInformation("SRT translation verification completed with score: {Score}", qualityScore);
                        }
                        else
                        {
                            _logger.LogWarning("SRT translation verification failed: {Error}", verificationResult.ErrorMessage);
                            qualityScore = 0.7;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during SRT translation verification");
                        qualityScore = 0.7;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating SRT file");
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating SRT file: {ex.Message}" };
            }

            
        }
        
        private async Task<List<string>> TranslateSrtContentInChunks(List<string> subtitleTexts, LanguageModel targetLanguage, CancellationToken cancellationToken)
        {
            const int maxChunkSize = 50;
            const int maxCharacters = 8000;
    
            var chunks = new List<List<string>>();
            var currentChunk = new List<string>();
            var currentCharacterCount = 0;
    
            foreach (var text in subtitleTexts)
            {
                if ((currentChunk.Count >= maxChunkSize) || 
                    (currentCharacterCount + text.Length > maxCharacters && currentChunk.Count > 0))
                {
                    chunks.Add([..currentChunk]);
                    currentChunk.Clear();
                    currentCharacterCount = 0;
                }
        
                currentChunk.Add(text);
                currentCharacterCount += text.Length + 2;
            }
    
            if (currentChunk.Count > 0)
            {
                chunks.Add(currentChunk);
            }
    
            _logger.LogInformation("Split {TotalSubtitles} subtitles into {ChunkCount} chunks for translation", 
                subtitleTexts.Count, chunks.Count);
    
            var allTranslatedTexts = new List<string>();
    
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var combinedChunkText = string.Join("\n\n", chunk);
        
                _logger.LogInformation("Translating SRT chunk {ChunkIndex}/{ChunkCount} with {SubtitleCount} subtitles", 
                    i + 1, chunks.Count, chunk.Count);
        
                try
                {
                    var translatedChunk = await TranslateSrtChunk(combinedChunkText, targetLanguage.Name, i + 1, chunks.Count, cancellationToken);
            
                    if (string.IsNullOrWhiteSpace(translatedChunk))
                    {
                        _logger.LogWarning("Empty translation result for SRT chunk {ChunkIndex}", i + 1);
                        allTranslatedTexts.AddRange(chunk);
                        continue;
                    }
            
                    var translatedLines = translatedChunk.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
            
                    if (translatedLines.Length != chunk.Count)
                    {
                        _logger.LogWarning("Translation count mismatch for chunk {ChunkIndex}. Expected: {Expected}, Got: {Actual}", 
                            i + 1, chunk.Count, translatedLines.Length);
                
                        for (int j = 0; j < chunk.Count; j++)
                        {
                            if (j < translatedLines.Length && !string.IsNullOrWhiteSpace(translatedLines[j]))
                            {
                                allTranslatedTexts.Add(translatedLines[j].Trim());
                            }
                            else
                            {
                                allTranslatedTexts.Add(chunk[j]);
                            }
                        }
                    }
                    else
                    {
                        allTranslatedTexts.AddRange(translatedLines.Select(line => line.Trim()));
                    }
            
                    _logger.LogInformation("Successfully translated SRT chunk {ChunkIndex}/{ChunkCount}", i + 1, chunks.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error translating SRT chunk {ChunkIndex}", i + 1);
                    allTranslatedTexts.AddRange(chunk);
                }
            }
    
            _logger.LogInformation("Completed chunked SRT translation. Original: {OriginalCount}, Translated: {TranslatedCount}", 
                subtitleTexts.Count, allTranslatedTexts.Count);
    
            return allTranslatedTexts;
        }
        
        private async Task<string> TranslateSrtChunk(string chunkText, string targetLanguage, int chunkNumber, int totalChunks, CancellationToken cancellationToken)
        {
            try
            {
                var prompt = GenerateSrtChunkTranslationPrompt(targetLanguage, chunkText, chunkNumber, totalChunks);
        
                var message = new ContentFile { Type = "text", Text = prompt };
                var claudeRequest = new ClaudeRequestWithFile([message]);
        
                _logger.LogInformation("Sending SRT chunk {ChunkNumber}/{TotalChunks} to Claude for translation", chunkNumber, totalChunks);
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    _logger.LogWarning("No content received from Claude for SRT chunk {ChunkNumber}", chunkNumber);
                    return string.Empty;
                }
        
                string translatedText = content.Text.Trim();
        
                if (translatedText.StartsWith("Here", StringComparison.OrdinalIgnoreCase) || 
                    translatedText.StartsWith("I've translated", StringComparison.OrdinalIgnoreCase))
                {
                    int firstLineBreak = translatedText.IndexOf('\n');
                    if (firstLineBreak > 0)
                    {
                        translatedText = translatedText.Substring(firstLineBreak + 1).Trim();
                    }
                }
        
                _logger.LogInformation("Successfully translated SRT chunk {ChunkNumber}, length: {Length} characters", chunkNumber, translatedText.Length);
                return translatedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating SRT chunk {ChunkNumber} with Claude", chunkNumber);
                return string.Empty;
            }
        }

        private string RebuildSrtFileWithTranslatedTexts(List<SrtEntry> entries, List<string> translatedTexts)
        {
            var result = new StringBuilder();
    
            for (int i = 0; i < entries.Count; i++)
            {
                result.AppendLine(entries[i].SequenceNumber.ToString());
                result.AppendLine(entries[i].Timestamp);
        
                if (i < translatedTexts.Count && !string.IsNullOrWhiteSpace(translatedTexts[i]))
                {
                    result.AppendLine(translatedTexts[i]);
                }
                else
                {
                    result.AppendLine(entries[i].Text); 
                }
        
                result.AppendLine(); 
            }
    
            return result.ToString().TrimEnd();
        }


        
        private List<SrtEntry> ParseSrtContent(string srtContent)
        {
            var entries = new List<SrtEntry>();
            var lines = srtContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    
            for (int i = 0; i < lines.Length; i++)
            {
                // Look for a sequence number
                if (int.TryParse(lines[i].Trim(), out int sequenceNumber))
                {
                    // The next line should be timestamped
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
                You are a meticulous Quality Assurance Specialist for translations.
                Your task is to compare two translations into {targetLanguage} and determine which one is of higher quality.

                TRANSLATION A:
                {translatedText}

            TRANSLATION B:
            {improvedTranslation}

            Evaluate rigorously based on the following criteria:
                1.  **Fluency and Naturalness**: How well does the translation flow in {targetLanguage}? Does it sound natural to a native speaker?
                2.  **Terminology Consistency**: Is terminology used consistently throughout each translation?
                3.  **Completeness**: Are there any untranslated or missing parts from the source (assuming you could infer the source's scope from the translations)?
                4.  **Overall Accuracy and Quality**: Considering all factors, which translation more accurately and effectively conveys the likely meaning of the source text in {targetLanguage}?

                Respond with either 'A' or 'B' to indicate which translation is unequivocally better. Follow this with a concise explanation for your choice, highlighting the key differentiators.
                Strictly adhere to the following response format: <A or B>|<explanation>
                Example: A|Translation A is more fluent and uses more natural phrasing for the target language.
                ";
        }

        private static string GenerateImprovedTranslationPrompt(string translatedText, string targetLanguage, string feedback)
        {
            return $"""
                You are an expert {targetLanguage} translator and editor, tasked with refining a translation based on quality review feedback.

                **Quality Review Feedback:**
                {feedback}

            **Your Objective:**
                Improve the provided translation by meticulously addressing the issues highlighted in the feedback.
                Focus on the following key areas to elevate the translation's quality:

                1.  **Untranslated Content**: Identify and translate any words or phrases that were mistakenly left untranslated.
                2.  **Terminology Cohesion**: Ensure uniform and consistent use of terminology throughout the entire document.
                3.  **Natural Phrasing**: Rectify any awkward or unnatural phrasing to ensure the text flows smoothly and idiomatically in {targetLanguage}.
                4.  **Formatting Integrity**: Preserve and correct any inconsistencies in formatting and structure.
                5.  **Technical Accuracy**: Verify and correct translations of technical terms using standard {targetLanguage} equivalents.
                6.  **Preservation of Identifiers**: CRITICAL: Technical identifiers, standards (e.g., ISO, EN), codes, and reference numbers MUST be preserved in their original form and MUST NOT be translated.

                **Instructions:**
                -   Return ONLY the complete, improved translation.
                -   DO NOT include any introductory text, explanations, apologies, or summaries of changes.
                -   Your output must be solely the final, polished {targetLanguage} text.

                **Current Translation for Improvement:**
                {translatedText}
            """;
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

            return $"""
                You are an advanced OCR (Optical Character Recognition) and translation system.
                Your task is to process an image snippet from a document.

                **Document Context:**
                -   Page Number: {pageNumber}
                -   Section of Page: {sectionName} (representing approximately the {verticalPortion} vertical portion of the page)

                **Required Actions:**
                1.  **Extract Text**: Accurately extract ALL visible textual content from the provided image section.
                2.  **Translate**: Translate the entire extracted text into **{targetLanguageName}**.
                3.  **Format as Markdown**: Present the translated content in well-structured Markdown:
                * Headings: Use `#` syntax (e.g., `# Main Heading`, `## Subheading`).
                * Lists: Use `-` or `*` for bullet points, or numbered lists (e.g., `1. Item`).
                * Emphasis: Use `**bold**` for strong emphasis and `*italic*` for regular emphasis.
                * Tables: If tabular data is present, format it using Markdown table syntax.
                * Separators: Use horizontal rules (`---`) to logically separate distinct content sections where appropriate.
                * Code/Technical Blocks: Format code snippets or highly structured technical content using triple backticks (```).
                4.  **Preserve Original Data**:
                * Keep all numbers, dates, and specific codes (that are not technical standards to be preserved) exactly as they appear in the original, or transliterate them appropriately if they are part of a sentence structure that requires it in {targetLanguageName}.
                5.  **CRITICAL - Non-Translation Rules**:
                * **DO NOT TRANSLATE** the following items:
                * Technical identifiers.
                * Standards (e.g., ISO, EN, ДСТУ, ГОСТ).
                * Specific codes (e.g., ДФРПОУ, НААУ).
                * Reference numbers or part numbers.
                * These items must be preserved in their original form.
                6.  **Proper Nouns**: Transliterate proper nouns (names of people, organizations, specific places) according to standard {targetLanguageName} conventions if a common translation doesn't exist. If a well-known translation exists, use it.
                7.  **Contextual Structure**: Use contextual clues from the extracted text to infer and apply the correct document structure (titles, sections, lists, paragraphs, etc.) in your Markdown output.

                **Output Requirements:**
                -   Provide ONLY the translated text in {targetLanguageName}.
                -   The output MUST be formatted exclusively in Markdown.
                -   DO NOT include any explanations, remarks, or any portion of the original (untranslated) text.
                -   DO NOT add any introductory or concluding phrases.
                """;
        }

        private static string GenerateTranslationPrompt(LanguageModel language, int i, List<string> chunks, string chunk)
        {
            return $"""
                You are an expert multilingual document translator and formatter, specializing in converting OCR-extracted text into polished {language.Name} documents.
                You will receive a chunk of text extracted via OCR from a PDF document.

                This is chunk {i + 1} of {chunks.Count}.

                **Your Core Task:**
                Translate the provided text chunk into **{language.Name}** while meticulously adhering to the following detailed instructions.

                **Detailed Instructions:**

                1.  **Comprehensive Translation**: Translate **ALL** textual content into **{language.Name}**. No part of the translatable text should be omitted.
                2.  **Layout Preservation**: Strive to **preserve the original formatting and layout** of the text as closely as possible using Markdown. This includes paragraph structure, line breaks where meaningful, and overall visual organization.
                3.  **Data Integrity (Numbers, Dates, Codes)**:
                * Keep **all numbers, dates, general codes, and identifiers** (that are not covered by point 4) exactly as they appear in the source, or transliterate/format them as contextually appropriate for {language.Name}.
                4.  **Non-Translatable Elements (CRITICAL)**:
                * **DO NOT TRANSLATE** any of the following:
                * Technical identifiers (e.g., part numbers, model numbers).
                * Official Standards (e.g., ISO 9001, EN 10025, ДСТУ Б В.2.7-170:2008).
                * Specific Codes (e.g., ДФРПОУ, НААУ, EAN codes).
                * Reference numbers.
                * These elements MUST be reproduced exactly as they appear in the source text.
                5.  **OCR Artifact Handling**:
                * Analyze and **correct common OCR artifacts** (e.g., misrecognized characters, jumbled words) using contextual understanding to infer the correct form.
                * If an artifact is ambiguous but seems to be part of the original, transcribe it faithfully. Do not introduce unrelated characters.
                6.  **Structural Fidelity**: Maintain **paragraph breaks, line spacing (represented by newlines in Markdown), and the general sequence** of text elements as in the original.
                7.  **Proper Nouns**: **Transliterate** proper nouns (names of people, companies, specific locations) according to {language.Name} linguistic norms and conventions, unless a standard, widely accepted translation in {language.Name} exists.
                8.  **Technical Terminology**: Employ **standard and accepted technical terms** in {language.Name} to ensure accuracy and professionalism.
                9.  **Duplicate Text**: If you encounter **duplicate text** within the chunk, **translate it as it appears**. Do not remove repetitions.
                10. **Markdown Formatting**: No matter which language you are translating to always Format the entire output using **Markdown** suitable for `README.md` files or similar documentation platforms.
                * Headings: Use `#`, `##`, etc.
                * Lists: Use `-` or `*` for unordered lists; `1.`, `2.` for ordered lists.
                * Tables: If data is clearly tabular, represent it using Markdown table syntax.
                * Code Blocks: Use triple backticks (```) for sections that are clearly forms, certificates, code, or highly structured data.
                * Horizontal Rules: Use `---` to denote significant breaks or transitions between sections if implied by the source.
                * Line Breaks: Preserve meaningful line breaks from the source; use double spaces at the end of a line for a `<br>` if needed, or ensure new paragraphs are distinct.

                **Output Constraints (Strictly Enforce):**
                    * Return **ONLY the final translated text** in {language.Name}.
                    * **NO** English explanations, remarks, comments, or summaries.
                    * **NO** original untranslated text.
                    * **NO** introductory or concluding statements.
                    * Ensure **no content is skipped or left untranslated** (unless specified as non-translatable).

                    Here is the OCR-extracted text (chunk {i + 1} of {chunks.Count}):

                    {chunk}
            """;
        }

        private static string GenerateFinalPrompt(string targetLanguageName, string finalCombination)
        {
            return $"""
                You are an expert document assembler and Markdown formatting specialist.
                You have been provided with multiple translated text chunks that now need to be meticulously combined into a single, coherent, and well-structured document in {targetLanguageName}.

                **Your Task:**
                1.  **Combine Chunks**: Integrate the provided text chunks into one seamless document.
                2.  **Eliminate Overlap**: Identify and intelligently remove any redundant or overlapping content that may exist between the concatenated chunks. Ensure that information is presented once, correctly.
                3.  **Ensure Cohesion and Flow**: The final document must flow naturally and logically. Maintain or establish proper transitions between sections.
                4.  **Preserve and Enhance Markdown**:
                * All existing Markdown formatting (headings, lists, tables, code blocks, emphasis) MUST be preserved.
                * **Based on the overall context and structure of the combined text, apply or refine Markdown formatting (e.g., add missing headings with `#` syntax, structure lists, use `---` for clear section breaks) to significantly improve readability and professional presentation.**
                5.  **Maintain Content Integrity**: DO NOT add any new textual content, comments, or remarks that were not present in the original chunks. Your role is to combine and format, not to create new information.
                6.  **Final Output**: Return ONLY the final, combined, and polished document in {targetLanguageName}, formatted entirely in Markdown. No other text or explanation should precede or follow the document.

                Here are the translated chunks to combine and refine:

                {finalCombination}
            """;
        }

        private static string GenerateDocumentCombinationPrompt(string targetLanguageName, string chunkContent)
        {
            return $"""
                You are an intelligent document reconstruction expert, proficient in {targetLanguageName} and Markdown.
                I have extracted and translated various potentially overlapping sections from a document. Each section is marked with its original page and location (e.g., top, middle, bottom).

                Your mission is to synthesize these sections into a single, perfectly ordered, and de-duplicated document.

                **Key Objectives:**

                1.  **Combine and Order**: Merge all provided sections into a single, cohesive document in {targetLanguageName}. Use the page and section markers (e.g., "PAGE 1 TOP", "PAGE 1 MIDDLE", "PAGE 2 TOP") as the absolute guide for correct sequencing.
                2.  **Eliminate Redundancy**: Carefully identify and remove any duplicate or overlapping content that occurs where sections meet. Ensure each piece of information appears only once in its correct place.
                3.  **Preserve Document Structure**: Maintain the inherent structure of the document (headings, paragraphs, lists, tables, etc.) as suggested by the content and any existing Markdown.
                4.  **Ensure Natural Flow**: The final document should read smoothly and logically from one part to the next.
                5.  **Maintain Markdown Formatting**: All Markdown formatting (headings, lists, tables, bold, italics, etc.) present in the chunks must be preserved and consistently applied.
                6.  **Content Fidelity**: Crucially, DO NOT add any content or information that was not present in the original sections provided.
                7.  **Clean Output**: DO NOT include the page and section markers (e.g., "--- PAGE 1 TOP ---") in your final output. These are for your guidance only.

                **Output Requirements:**
                -   Return ONLY the final, seamlessly combined document.
                -   The entire output must be in {targetLanguageName} and formatted using Markdown.
                -   Absolutely NO explanations, notes, or comments about your process.

                Here are the document sections to combine:

                {chunkContent}
            """;
        }

        private static string GenerateSrtChunkTranslationPrompt(string targetLanguage, string chunkText, int chunkNumber, int totalChunks)
        {
            return $"""
                You are an expert subtitle translator specializing in creating accurate and natural-sounding subtitles for {targetLanguage} video content.
                You will be provided with a chunk of subtitle text entries from an SRT file.

                This is chunk {chunkNumber} of {totalChunks} total chunks.

                **Your Task:**
                Translate ALL subtitle text entries into **{targetLanguage}**, adhering to the highest standards of subtitle quality.

                **Critical Guidelines for Subtitle Translation:**

                1.  **Accurate Translation**: Translate the meaning of the original text faithfully.
                2.  **Conciseness and Readability**: Subtitles must be concise and easy to read quickly on screen. Use clear and straightforward language.
                3.  **Entry Count Integrity**: The number of translated subtitle entries MUST EXACTLY MATCH the number of original subtitle entries provided in this chunk. Do not merge or split entries.
                4.  **Natural Language**: Use conversational and natural phrasing appropriate for {targetLanguage} spoken dialogue or narration.
                5.  **Tone and Emphasis**: Preserve the original emphasis, emotion, and tone of the dialogue/narration wherever possible within the constraints of subtitling.
                6.  **Line Breaks**: If individual subtitle entries contain internal line breaks, preserve them in the translated version.
                7.  **Proper Nouns**: Generally, DO NOT translate proper names (people, specific places, brands) unless they have a widely recognized and standard translation in {targetLanguage}. If not, transliterate them appropriately.
                8.  **Character Limits (Implied)**: While not explicitly given, translate with an awareness that subtitles have limited screen time. Avoid overly long translations for short lines.

                **Output Format:**
                -   Return ONLY the translated subtitle text entries.
                -   Each translated subtitle entry should be separated from the next by a double line break (`\n\n`).
                -   DO NOT include any explanations, notes, original text, or any numbering/timestamps.

                Here are the subtitle texts to translate (each entry is separated by a double line break):

                {chunkText}
            """;
        } 
    }
}
