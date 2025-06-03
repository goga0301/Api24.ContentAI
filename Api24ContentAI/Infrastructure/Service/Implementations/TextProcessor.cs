using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class TextProcessor(IClaudeService claudeService, ILanguageService languageService,
    IUserService userService,
    IGptService gptService,
    ILogger<DocumentTranslationService> logger
) : ITextProcessor
{
    private readonly IClaudeService _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userRepository = userService ?? throw new ArgumentNullException(nameof(userService));
    private readonly IGptService _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
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

            var translatedContent = await TranslateTextContent(textContent, targetLanguage, cancellationToken);

            if (string.IsNullOrWhiteSpace(translatedContent))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation failed - no content returned" };
            }

            string improvedTranslation = translatedContent;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedContent))
            {
                _logger.LogInformation("Starting text translation verification");
                
                try
                {
                    var sampleText = translatedContent.Length > 2000 
                        ? translatedContent.Substring(0, 2000) + "..."
                        : translatedContent;
                    
                    var verificationResult = await _gptService.EvaluateTranslationQuality(
                        $"Evaluate this text translation to {targetLanguage.Name}. Focus on accuracy, fluency, and preservation of meaning:\n\n{sampleText}", 
                        cancellationToken);
                    
                    if (verificationResult.Success)
                    {
                        qualityScore = verificationResult.QualityScore ?? 1.0;
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

    private async Task<string> TranslateTextContent(string textContent, LanguageModel targetLanguage, CancellationToken cancellationToken)
    {
        const int maxChunkSize = 8000;
        
        if (textContent.Length <= maxChunkSize)
        {
            return await TranslateTextChunk(textContent, targetLanguage.Name, 1, 1, cancellationToken);
        }

        var chunks = SplitTextIntoChunks(textContent, maxChunkSize);
        _logger.LogInformation("Split text into {ChunkCount} chunks for translation", chunks.Count);

        var translatedChunks = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            
            _logger.LogInformation("Translating text chunk {ChunkIndex}/{ChunkCount} with {CharacterCount} characters", 
                i + 1, chunks.Count, chunk.Length);
            
            try
            {
                var translatedChunk = await TranslateTextChunk(chunk, targetLanguage.Name, i + 1, chunks.Count, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(translatedChunk))
                {
                    _logger.LogWarning("Empty translation result for text chunk {ChunkIndex}", i + 1);
                    translatedChunks.Add(chunk); // Fallback to original
                }
                else
                {
                    translatedChunks.Add(translatedChunk);
                }
                
                _logger.LogInformation("Successfully translated text chunk {ChunkIndex}/{ChunkCount}", i + 1, chunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating text chunk {ChunkIndex}", i + 1);
                translatedChunks.Add(chunk);
            }
        }

        var fullTranslation = string.Join("", translatedChunks);
        _logger.LogInformation("Completed chunked text translation. Original: {OriginalLength}, Translated: {TranslatedLength}", 
            textContent.Length, fullTranslation.Length);

        return fullTranslation;
    }

    private async Task<string> TranslateTextChunk(string chunkText, string targetLanguage, int chunkNumber, int totalChunks, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = GenerateTextChunkTranslationPrompt(targetLanguage, chunkText, chunkNumber, totalChunks);
            
            var message = new ContentFile { Type = "text", Text = prompt };
            var claudeRequest = new ClaudeRequestWithFile([message]);
            
            _logger.LogInformation("Sending text chunk {ChunkNumber}/{TotalChunks} to Claude for translation", chunkNumber, totalChunks);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                _logger.LogWarning("No content received from Claude for text chunk {ChunkNumber}", chunkNumber);
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
            
            _logger.LogInformation("Successfully translated text chunk {ChunkNumber}, length: {Length} characters", chunkNumber, translatedText.Length);
            return translatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating text chunk {ChunkNumber} with Claude", chunkNumber);
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
                3. **Preserve Structure**: Maintain the original text structure, including paragraphs, line breaks, and formatting.
                4. **Tone and Style**: Preserve the original tone, style, and register of the text (formal, informal, technical, etc.).
                5. **Context Awareness**: Consider the context and ensure translations make sense within the broader narrative.
                6. **Proper Nouns**: Generally keep proper names (people, places, brands) unchanged unless they have standard translations in {targetLanguage}.
                7. **Technical Terms**: Translate technical terms appropriately, using standard terminology in {targetLanguage}.
                8. **Completeness**: Ensure no content is omitted or added beyond what's necessary for natural translation.

                **Output Format:**
                - Return ONLY the translated text.
                - Maintain the exact same structure and formatting as the original.
                - DO NOT include any explanations, notes, or commentary.

                Here is the text to translate:

                {chunkText}
                """;
    }
}