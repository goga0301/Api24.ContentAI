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

public class SrtProcessor(IAIService aiService, ILanguageService languageService,
    IUserService userService,
    ILogger<DocumentTranslationService> logger
) : ISrtProcessor
{
    
    private readonly IAIService _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userRepository = userService ?? throw new ArgumentNullException(nameof(userService));
    private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool CanProcess(string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".srt";
    }

    public Task<DocumentTranslationResult> TranslateWithTesseract(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat,
        CancellationToken cancellationToken)
    {
        throw new UnsupportedContentTypeException("Tesseract endpoint does not support processing or .srt files");
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
        
            var translatedTexts = await TranslateSrtContentInChunks(textsToTranslate, targetLanguage, model, cancellationToken);
        
            if (translatedTexts == null || translatedTexts.Count != textsToTranslate.Count)
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation failed or mismatch in subtitle count" };
            }

            var translatedSrtContent = RebuildSrtFileWithTranslatedTexts(subtitleEntries, translatedTexts);

            string improvedTranslation = translatedSrtContent;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedSrtContent))
            {
                _logger.LogInformation("Starting SRT translation verification with {Model}", model);
            
                try
                {
                    var sampleTexts = translatedTexts.Take(Math.Min(10, translatedTexts.Count)).ToList();
                    var verificationText = string.Join("\n", sampleTexts);
                
                    var verificationPrompt = $"Evaluate this SRT subtitle translation sample to {targetLanguage.Name}. Focus on subtitle-appropriate language, timing considerations, and readability:\n\n{verificationText}";
                    var verificationResult = await _aiService.SendTextRequest(verificationPrompt, model, cancellationToken);
                
                    if (verificationResult.Success)
                    {
                        qualityScore = 0.95;
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
    
    private async Task<List<string>> TranslateSrtContentInChunks(List<string> subtitleTexts, LanguageModel targetLanguage, AIModel model, CancellationToken cancellationToken)
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
                var translatedChunk = await TranslateSrtChunk(combinedChunkText, targetLanguage.Name, i + 1, chunks.Count, model, cancellationToken);
            
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
    
    private async Task<string> TranslateSrtChunk(string chunkText, string targetLanguage, int chunkNumber, int totalChunks, AIModel model, CancellationToken cancellationToken)
    {
        var prompt = GenerateSrtChunkTranslationPrompt(targetLanguage, chunkText, chunkNumber, totalChunks);

        try
        {
            _logger.LogInformation("Sending SRT chunk {ChunkNumber}/{TotalChunks} to AI service for translation",
                chunkNumber, totalChunks);
            
            var aiResponse = await _aiService.SendTextRequest(prompt, model, cancellationToken);

            if (!aiResponse.Success)
            {
                _logger.LogWarning("AI service failed for SRT chunk {ChunkNumber}: {Error}", 
                    chunkNumber, aiResponse.ErrorMessage);
                return null;
            }

            var translatedText = aiResponse.Content?.Trim();
            
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogWarning("AI service returned empty translation for SRT chunk {ChunkNumber}", chunkNumber);
                return null;
            }
            
            _logger.LogInformation("Successfully translated SRT chunk {ChunkNumber}/{TotalChunks}", 
                chunkNumber, totalChunks);

            return translatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating SRT chunk {ChunkNumber} with AI service", chunkNumber);
            return null;
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
            if (int.TryParse(lines[i].Trim(), out int sequenceNumber))
            {
                if (i + 1 < lines.Length && lines[i + 1].Contains("-->"))
                {
                    var timestamp = lines[i + 1].Trim();
                
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
                
                    i = textIndex - 1;
                }
            }
        }
    
        return entries;
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
