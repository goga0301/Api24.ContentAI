using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class ImageProcessor(
    IClaudeService claudeService,
    ILanguageService languageService,
    IUserService userService,
    IGptService gptService,
    ILogger<DocumentTranslationService> logger,
    HttpClient httpClient
) : IImageProcessor
{
    private readonly IClaudeService _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    private readonly IGptService _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
    private readonly ILogger<DocumentTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public bool CanProcess(string fileExtension)
    {
        var ext = fileExtension.ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
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

            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);
            if (targetLanguage == null)
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language not found" };
            }

            _logger.LogInformation("Processing image file {FileName} for OCR and translation to {LanguageName}",
                file.FileName, targetLanguage.Name);

            // Step 1: Get OCR text from microservice
            using var content = new MultipartFormDataContent();
            using var fileStream = file.OpenReadStream();
            using var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", file.FileName);

            var response = await _httpClient.PostAsync("http://localhost:8000/ocr-image", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("OCR service returned error: {Error}", error);
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"OCR service error: {error}" };
            }

            var ocrResult = await response.Content.ReadFromJsonAsync<OcrResponse>(cancellationToken: cancellationToken);
            if (ocrResult == null || string.IsNullOrWhiteSpace(ocrResult.Text))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "OCR service returned empty result" };
            }

            // Step 2: Send OCR text to Claude for translation
            var prompt = GenerateTranslationPrompt(ocrResult.Text, targetLanguage.Name);
            var claudeRequest = new ClaudeRequest(prompt);

            _logger.LogInformation("Sending OCR text to Claude for translation");
            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

            var claudeContent = claudeResponse.Content?.SingleOrDefault();
            if (claudeContent == null)
            {
                _logger.LogWarning("No content received from Claude service for translation");
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service failed to process the text" };
            }

            string translatedText = claudeContent.Text.Trim();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service returned empty result" };
            }

            string translationId = Guid.NewGuid().ToString();

            var translationResult = new DocumentTranslationResult
            {
                Success = true,
                OriginalContent = ocrResult.Text,
                TranslatedContent = translatedText,
                OutputFormat = Domain.Models.DocumentFormat.Markdown,
                FileData = Encoding.UTF8.GetBytes(translatedText),
                FileName = $"translated_{translationId}.md",
                ContentType = "text/markdown",
                TranslationId = translationId
            };

            _logger.LogInformation("Image OCR and translation completed successfully");
            return translationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image file with OCR and translation");
            return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error processing image file: {ex.Message}" };
        }
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

            var user = await _userService.GetById(userId, cancellationToken);
            if (user == null)
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "User not found" };
            }

            var targetLanguage = await _languageService.GetById(targetLanguageId, cancellationToken);
            if (targetLanguage == null)
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language not found" };
            }

            _logger.LogInformation("Processing image file {FileName} for translation to {LanguageName}",
                file.FileName, targetLanguage.Name);

            // Convert image to base64
            string base64Image;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, cancellationToken);
                base64Image = Convert.ToBase64String(ms.ToArray());
            }

            var prompt = GenerateImageTranslationPrompt(targetLanguage.Name, base64Image);
            var mediaType = file.FileName.ToLowerInvariant() switch
            {
                var f when f.EndsWith(".png") => "image/png",
                var f when f.EndsWith(".jpg") || f.EndsWith(".jpeg") => "image/jpeg",
                _ => throw new ArgumentException($"Unsupported image format: {file.FileName}")
            };
            var promptFile = new ContentFile
            {
                Type = "text",
                Text = prompt
            };
            var imageFile = new ContentFile
            {
                Type = "image",
                Source = new Source
                {
                    Type = "base64",
                    Data = base64Image,
                    MediaType = mediaType
                }
            };
            var claudeRequest = new ClaudeRequestWithFile([promptFile, imageFile]);

            _logger.LogInformation("Sending image to Claude for translation");
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);

            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                _logger.LogWarning("No content received from Claude service for image translation");
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service failed to process the image" };
            }

            string translatedText = content.Text.Trim();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service returned empty result" };
            }

            string improvedTranslation = translatedText;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogInformation("Starting image translation verification");

                try
                {
                    var sampleText = translatedText.Length > 2000
                        ? translatedText.Substring(0, 2000) + "..."
                        : translatedText;

                    var verificationResult = await _gptService.EvaluateTranslationQuality(
                        $"Evaluate this text translation to {targetLanguage.Name}. Focus on accuracy, fluency, and preservation of meaning:\n\n{sampleText}",
                        cancellationToken);

                    if (verificationResult.Success)
                    {
                        qualityScore = verificationResult.QualityScore ?? 1.0;
                        _logger.LogInformation("Image translation verification completed with score: {Score}", qualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("Image translation verification failed: {Error}", verificationResult.ErrorMessage);
                        qualityScore = 0.7;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during image translation verification");
                    qualityScore = 0.7;
                }
            }

            string translationId = Guid.NewGuid().ToString();

            var result = new DocumentTranslationResult
            {
                Success = true,
                OriginalContent = translatedText,
                TranslatedContent = improvedTranslation,
                OutputFormat = Domain.Models.DocumentFormat.Markdown,
                FileData = Encoding.UTF8.GetBytes(improvedTranslation),
                FileName = $"translated_{translationId}.md",
                ContentType = "text/markdown",
                TranslationQualityScore = qualityScore,
                TranslationId = translationId
            };

            _logger.LogInformation("Image translation completed successfully. Quality score: {Score}", qualityScore);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating image file");
            return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating image file: {ex.Message}" };
        }
    }

    private static string GenerateImageTranslationPrompt(string targetLanguage, string base64Image)
    {
        return $"""
            Translate this document to {targetLanguage} in Markdown format.
            
            IMPORTANT: Do not summarize or analyze - translate every piece of text you see.
            
            Use appropriate Markdown formatting:
            - # ## ### for headers and titles
            - **bold** for emphasis
            - - or * for bullet lists
            - | tables | when needed
            - ``` for code blocks or structured data
            - Preserve line breaks and document structure
            
            Keep numbers, dates, codes, and official identifiers unchanged.
            
            Output only the translated Markdown - no explanations.
            """;
    }

    private string GenerateTranslationPrompt(string text, string targetLanguage)
    {
        return $"""
                    You are an expert multilingual document translator and formatter, specializing in converting OCR-extracted text into polished {targetLanguage} documents.
                    You will receive text extracted via OCR from an image.

                    **Your Core Task:**
                    Translate the provided text into **{targetLanguage}** while meticulously adhering to the following detailed instructions.

                    **Detailed Instructions:**

                    1.  **Comprehensive Translation**: Translate **ALL** textual content into **{targetLanguage}**. No part of the translatable text should be omitted.
                    2.  **Layout Preservation**: Strive to **preserve the original formatting and layout** of the text as closely as possible using Markdown. This includes paragraph structure, line breaks where meaningful, and overall visual organization.
                    3.  **Data Integrity (Numbers, Dates, Codes)**:
                    * Keep **all numbers, dates, general codes, and identifiers** (that are not covered by point 4) exactly as they appear in the source, or transliterate/format them as contextually appropriate for {targetLanguage}.
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
                    7.  **Proper Nouns**: **Transliterate** proper nouns (names of people, companies, specific locations) according to {targetLanguage} linguistic norms and conventions, unless a standard, widely accepted translation in {targetLanguage} exists.
                    8.  **Technical Terminology**: Employ **standard and accepted technical terms** in {targetLanguage} to ensure accuracy and professionalism.
                    9.  **Duplicate Text**: If you encounter **duplicate text** within the chunk, **translate it as it appears**. Do not remove repetitions.
                    10. **Markdown Formatting**: No matter which language you are translating to always Format the entire output using **Markdown** suitable for `README.md` files or similar documentation platforms.
                    * Headings: Use `#`, `##`, etc.
                    * Lists: Use `-` or `*` for unordered lists; `1.`, `2.` for ordered lists.
                    * Tables: If data is clearly tabular, represent it using Markdown table syntax.
                    * Code Blocks: Use triple backticks (```) for sections that are clearly forms, certificates, code, or highly structured data.
                    * Horizontal Rules: Use `---` to denote significant breaks or transitions between sections if implied by the source.
                    * Line Breaks: Preserve meaningful line breaks from the source; use double spaces at the end of a line for a `<br>` if needed, or ensure new paragraphs are distinct.

                    **Output Constraints (Strictly Enforce):**
                        * Return **ONLY the final translated text** in {targetLanguage}.
                        * **NO** English explanations, remarks, comments, or summaries.
                        * **NO** original untranslated text.
                        * **NO** introductory or concluding statements.
                        * Ensure **no content is skipped or left untranslated** (unless specified as non-translatable).

                        Here is the OCR-extracted text:

                        {text}
                """;
    }


    private class OcrResponse
    {
        public string Text { get; set; }
        public string Error { get; set; }
    }
}
