using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

        public async Task<DocumentTranslationResult> TranslateDocument(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
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

                var ocrJsonContent = await SendFileToOcrService(file, cancellationToken);
                
                var translationResult = await TranslateOcrJson(ocrJsonContent, targetLanguageId, userId, cancellationToken);
                
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
                formContent.Add(new StringContent("true"), "autodetect");
                
                var response = await httpClient.PostAsync("http://127.0.0.1:8000/ocr", formContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Received OCR response, length: {Length} characters", 
                    responseContent?.Length ?? 0);
                
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
        
        public async Task<DocumentTranslationResult> TranslateOcrJson(string ocrJsonContent, int targetLanguageId, string userId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ocrJsonContent))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No OCR content provided" };
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };
                }

                _logger.LogInformation("Deserializing OCR JSON response");
                
                string extractedText;
                try
                {
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(ocrJsonContent);
                    var root = jsonDoc.RootElement;
                    
                    _logger.LogInformation("JSON structure: {Structure}", 
                        string.Join(", ", root.EnumerateObject().Select(p => p.Name)));
                    
                    extractedText = ExtractTextFromOcrJson(root);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing OCR JSON");
                    return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error parsing OCR JSON: {ex.Message}" };
                }
                
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning("Could not extract text from OCR response");
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Could not extract text from OCR response" };
                }
                
                _logger.LogInformation("Successfully extracted {Length} characters from OCR response", extractedText.Length);
                
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
                    
                    var prompt = $"""

                                                          You are a professional translator. I'm sending you OCR results extracted from a PDF document.

                                                          Your task:
                                                          1. Translate all the text content into {language.Name}
                                                          2. Preserve the exact formatting and layout of the original text
                                                          3. Keep all numbers, dates, codes, and relevant data from the context of text exactly as they appear
                                                          4. If you see some funny characters which are most likely OCR problems, ignore them or fix them appropriately
                                                          5. Maintain paragraph breaks and line spacing
                                                          6. Return the translated text with the same structure as the original
                                                          7. IMPORTANT: Do NOT translate technical identifiers, standards (like ISO, EN, ДСТУ, etc.), codes (like ЄДРПОУ, НААУ), and reference numbers - keep these in their original form
                                                          8. For proper nouns, transliterate according to {language.Name} conventions if appropriate
                                                          9. Ensure all technical terminology is translated using standard {language.Name} equivalents
                                                          10. DO NOT include any English explanations, remarks, or original text alongside your translation
                                                          11. DO NOT add any notes, comments, or clarifications in English or any other language
                                                          12. Provide ONLY the translated text in {language.Name}, with no additional commentary
                                                          13. IMPORTANT: if you see duplicate text, do not remove it your task is to translate the text, not to remove it or ADD any additional text
                                                          14. IMPORTANT: never left out any text from the original language untranslated or remove any text from the translated language

                                                          Here is the OCR extracted text (chunk {i + 1} of {chunks.Count}):

                                                          {chunk}
                                                          
                                  """;

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
                    
                    if (verificationResult.ChunkWarnings != null && verificationResult.ChunkWarnings.Count > 0)
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
                            var improvedChunks = new List<KeyValuePair<int, string>> { new(1, improvedTranslation) };
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
            
            StringBuilder currentChunk = new StringBuilder();
            
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

        private string ExtractTextFromOcrJson(System.Text.Json.JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("results", out var resultsElement) && resultsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var pageTexts = new List<string>();
                    var pageCount = 0;
                    
                    foreach (var result in resultsElement.EnumerateArray())
                    {
                        pageCount++;
                        if (result.TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                pageTexts.Add(text);
                                _logger.LogInformation("Added page {Index}: {Length} characters", pageCount, text.Length);
                                _logger.LogDebug("Page {Index} content sample: {Sample}", 
                                    pageCount, text.Length > 100 ? text.Substring(0, 100) + "..." : text);
                            }
                            else
                            {
                                _logger.LogWarning("Page {Index} has empty text", pageCount);
                            }
                        }
                        else if (result.TryGetProperty("page", out var pageElement) && 
                                 result.TryGetProperty("text", out var pageTextElement))
                        {
                            string text = pageTextElement.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                pageTexts.Add(text);
                                _logger.LogInformation("Added page {Index}: {Length} characters", 
                                    pageElement.GetInt32(), text.Length);
                            }
                            else
                            {
                                _logger.LogWarning("Page {Index} has empty text", pageElement.GetInt32());
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Result for page {Index} has no 'text' property", pageCount);
                            string extractedText = ExtractAllTextFromJson(result);
                            if (!string.IsNullOrWhiteSpace(extractedText))
                            {
                                pageTexts.Add(extractedText);
                                _logger.LogInformation("Extracted text from page {Index} using fallback method: {Length} characters", 
                                    pageCount, extractedText.Length);
                            }
                        }
                    }
                    
                    _logger.LogInformation("Found {Count} pages in OCR results", pageTexts.Count);
                    
                    if (pageTexts.Count > 0)
                    {
                        string resultText = string.Join("\n", pageTexts);
                        _logger.LogInformation("Extracted text from {Count} pages, total length: {Length} characters", 
                            pageTexts.Count, resultText.Length);
                        _logger.LogInformation("this is Content of extracted text \n {resultText}", resultText);
                        
                        return resultText;
                    }
                }
                
                _logger.LogWarning("Could not extract any text from OCR JSON. JSON structure: {Structure}", 
                    string.Join(", ", root.EnumerateObject().Select(p => p.Name)));
                
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from OCR JSON");
                return "";
            }
        }

        private string ExtractAllTextFromJson(System.Text.Json.JsonElement element, int depth = 0)
        {
            if (depth > 10) return "";
            
            var texts = new List<string>();
            
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    var text = element.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                    break;
                case System.Text.Json.JsonValueKind.Object:
                    texts.AddRange(element.EnumerateObject().Select(property => ExtractAllTextFromJson(property.Value, depth + 1)).Where(propertyText => !string.IsNullOrWhiteSpace(propertyText)));
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    texts.AddRange(element.EnumerateArray().Select(item => ExtractAllTextFromJson(item, depth + 1)).Where(itemText => !string.IsNullOrWhiteSpace(itemText)));
                    break;
            }
            
            return string.Join("\n\n", texts);
        }
        private async Task<string> ImproveTranslationWithFeedback(string translatedText, string targetLanguage, string feedback, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Attempting to improve translation based on GPT feedback");
                
                string prompt = $@"
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
                ";
                
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
                
                if (Math.Abs(improvedTranslation.Length - translatedText.Length) < translatedText.Length * 0.05)
                {
                    _logger.LogInformation("Improved translation has similar length to original, performing quality check");
                    
                    var verificationPrompt = $@"
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
                    
                    var verificationResult = await _gptService.EvaluateTranslationQuality(verificationPrompt, cancellationToken);
                    
                    if (verificationResult.Success && verificationResult.Feedback.StartsWith("A|"))
                    {
                        _logger.LogInformation("Verification indicates original translation was better, keeping original");
                        return string.Empty;
                    }
                }
                
                return improvedTranslation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error improving translation with feedback");
                return string.Empty;
            }
        }
    }
}
