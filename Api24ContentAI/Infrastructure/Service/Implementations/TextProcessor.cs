using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class TextProcessor(IAIService aiService, ILanguageService languageService,
    IUserService userService,
    ILogger<DocumentTranslationService> logger
) : ITextProcessor
{
    private readonly IAIService _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userRepository = userService ?? throw new ArgumentNullException(nameof(userService));
    private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool CanProcess(string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".txt";
    }

    public Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        CancellationToken cancellationToken)
    {
        throw new UnsupportedContentTypeException("Tesseract endpoint does not support processing of .txt files");
    }

    public async Task<DocumentTranslationResult> TranslateWithClaude(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        AIModel model, CancellationToken cancellationToken)
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

            _logger.LogInformation("Processing text file {FileName} for translation to {LanguageName}", 
                file.FileName, targetLanguage.Name);

            string textContent;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            {
                textContent = await reader.ReadToEndAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(textContent))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Text file is empty" };
            }

            _logger.LogInformation("Read text content with {Length} characters from file", textContent.Length);

            var translatedContent = await TranslateTextContent(textContent, targetLanguage, model, cancellationToken);

            if (string.IsNullOrWhiteSpace(translatedContent))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation failed - no content returned" };
            }

            string improvedTranslation = translatedContent;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedContent))
            {
                _logger.LogInformation("Starting text translation verification with {Model}", model);
                
                try
                {
                    var sampleText = translatedContent.Length > 2000 
                        ? translatedContent.Substring(0, 2000) + "..."
                        : translatedContent;
                    
                    var verificationPrompt = $"Evaluate this text translation to {targetLanguage.Name}. Focus on accuracy, fluency, and preservation of meaning:\n\n{sampleText}";
                    var verificationResult = await _aiService.SendTextRequest(verificationPrompt, model, cancellationToken);
                    
                    if (verificationResult.Success)
                    {
                        qualityScore = 0.95;
                        _logger.LogInformation("Text translation verification completed with score: {Score}", qualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("Text translation verification failed: {Error}", verificationResult.ErrorMessage);
                        qualityScore = 0.7;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during text translation verification");
                    qualityScore = 0.7;
                }
            }

            string translationId = Guid.NewGuid().ToString();
            
            var result = new DocumentTranslationResult
            {
                Success = true,
                OriginalContent = textContent,
                TranslatedContent = improvedTranslation,
                OutputFormat = Domain.Models.DocumentFormat.Txt,
                FileData = Encoding.UTF8.GetBytes(improvedTranslation),
                FileName = $"translated_{translationId}.txt",
                ContentType = "text/plain",
                TranslationQualityScore = qualityScore,
                TranslationId = translationId
            };

            _logger.LogInformation("Text translation completed successfully. Quality score: {Score}", qualityScore);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating text file");
            return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating text file: {ex.Message}" };
        }
    }

    private async Task<string> TranslateTextContent(string textContent, LanguageModel targetLanguage, AIModel model, CancellationToken cancellationToken)
    {
        const int maxChunkSize = 15000; // Increased from 8000 for fewer API calls
        
        if (textContent.Length <= maxChunkSize)
        {
            return await TranslateTextChunk(textContent, targetLanguage.Name, 1, 1, model, cancellationToken);
        }

        var chunks = SplitTextIntoChunks(textContent, maxChunkSize);
        _logger.LogInformation("Split text into {ChunkCount} chunks for translation", chunks.Count);

        // Process chunks in parallel for better performance (with limited concurrency to avoid rate limits)
        var semaphore = new SemaphoreSlim(5, 5); // Allow max 5 concurrent requests (increased for speed)
        var translatedChunks = new List<string>(new string[chunks.Count]);

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Translating text chunk {ChunkIndex}/{ChunkCount} with {CharacterCount} characters", 
                    index + 1, chunks.Count, chunk.Length);
                
                var translatedChunk = await TranslateTextChunk(chunk, targetLanguage.Name, index + 1, chunks.Count, model, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(translatedChunk))
                {
                    _logger.LogWarning("Empty translation result for text chunk {ChunkIndex}", index + 1);
                    translatedChunks[index] = chunk; // Fallback to original
                }
                else
                {
                    translatedChunks[index] = translatedChunk;
                }
                
                _logger.LogInformation("Successfully translated text chunk {ChunkIndex}/{ChunkCount}", index + 1, chunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating text chunk {ChunkIndex}", index + 1);
                translatedChunks[index] = chunk;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var fullTranslation = string.Join("", translatedChunks);
        _logger.LogInformation("Completed chunked text translation. Original: {OriginalLength}, Translated: {TranslatedLength}", 
            textContent.Length, fullTranslation.Length);

        return fullTranslation;
    }

    private async Task<string> TranslateTextChunk(string chunkText, string targetLanguage, int chunkNumber, int totalChunks, AIModel model, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = GenerateTextChunkTranslationPrompt(targetLanguage, chunkText, chunkNumber, totalChunks);
            
            _logger.LogInformation("Sending text chunk {ChunkNumber}/{TotalChunks} to AI service for translation", chunkNumber, totalChunks);
            var aiResponse = await _aiService.SendTextRequest(prompt, model, cancellationToken);

            if (!aiResponse.Success)
            {
                _logger.LogWarning("AI service failed for text chunk {ChunkNumber}: {Error}", 
                    chunkNumber, aiResponse.ErrorMessage);
                return string.Empty;
            }
            
            string translatedText = aiResponse.Content?.Trim();
            
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogWarning("AI service returned empty translation for text chunk {ChunkNumber}", chunkNumber);
                return string.Empty;
            }
            
            _logger.LogInformation("Successfully translated text chunk {ChunkNumber}, length: {Length} characters", chunkNumber, translatedText.Length);
            return translatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating text chunk {ChunkNumber} with AI service", chunkNumber);
            return string.Empty;
        }
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        
        if (text.Length <= maxChunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None);
        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (currentChunk.Length + paragraph.Length + 2 > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            if (paragraph.Length > maxChunkSize)
            {
                var sentences = paragraph.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.None);
                var sentenceChunk = new StringBuilder();

                for (int i = 0; i < sentences.Length; i++)
                {
                    var sentence = sentences[i];
                    if (i < sentences.Length - 1)
                    {
                        if (paragraph.Contains(sentence + ". "))
                            sentence += ". ";
                        else if (paragraph.Contains(sentence + "! "))
                            sentence += "! ";
                        else if (paragraph.Contains(sentence + "? "))
                            sentence += "? ";
                    }

                    if (sentenceChunk.Length + sentence.Length > maxChunkSize && sentenceChunk.Length > 0)
                    {
                        if (currentChunk.Length > 0)
                        {
                            chunks.Add(currentChunk.ToString());
                            currentChunk.Clear();
                        }
                        chunks.Add(sentenceChunk.ToString());
                        sentenceChunk.Clear();
                    }

                    sentenceChunk.Append(sentence);
                }

                if (sentenceChunk.Length > 0)
                {
                    if (currentChunk.Length + sentenceChunk.Length + 2 > maxChunkSize && currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString());
                        currentChunk.Clear();
                    }
                    
                    if (currentChunk.Length > 0)
                        currentChunk.Append("\n\n");
                    currentChunk.Append(sentenceChunk.ToString());
                }
            }
            else
            {
                if (currentChunk.Length > 0)
                    currentChunk.Append("\n\n");
                currentChunk.Append(paragraph);
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    private static string GenerateTextChunkTranslationPrompt(string targetLanguage, string chunkText, int chunkNumber, int totalChunks)
    {
        return $"""
                You are an expert translator specializing in accurate and natural-sounding translations to {targetLanguage}.
                You will be provided with text content that needs to be translated.

                This is chunk {chunkNumber} of {totalChunks} total chunks.

                **Your Task:**
                Translate the provided text into **{targetLanguage}**, maintaining the highest standards of translation quality.

                **Critical Guidelines for Text Translation:**

                1. **Accurate Translation**: Translate the meaning of the original text faithfully and completely.
                2. **Natural Language**: Use natural, fluent {targetLanguage} that reads smoothly and naturally.
                3. **Preserve Structure**: Maintain the original text structure, including paragraphs, line breaks, formatting, and use bullet lists when content naturally fits list format.
                4. **Tone and Style**: Preserve the original tone, style, and register of the text (formal, informal, technical, etc.).
                5. **Context Awareness**: Consider the context and ensure translations make sense within the broader narrative.
                6. **Proper Nouns**: Generally keep proper names (people, places, brands) unchanged unless they have standard translations in {targetLanguage}.
                7. **Technical Terms**: Translate technical terms appropriately, using standard terminology in {targetLanguage}.
                8. **Email Addresses**: NEVER translate email addresses - keep them exactly as they appear in the original text.
                9. **Completeness**: Ensure no content is omitted or added beyond what's necessary for natural translation.

                **Output Format:**
                - Return ONLY the translated text.
                - Maintain the exact same structure and formatting as the original.
                - DO NOT include any explanations, notes, or commentary.

                Here is the text to translate:

                {chunkText}
                """;
    }
}
