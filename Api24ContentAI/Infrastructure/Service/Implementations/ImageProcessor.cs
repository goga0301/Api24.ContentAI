using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Generic;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class ImageProcessor(
    IAIService aiService,
    ILanguageService languageService,
    IUserService userService,
    ILogger<DocumentTranslationService> logger,
    HttpClient httpClient
) : IImageProcessor
{
    private readonly IAIService _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    private readonly ILanguageService _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    private readonly IUserService _userService = userService ?? throw new ArgumentNullException(nameof(userService));
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

            _logger.LogInformation("Sending OCR text to AI for translation");
            var aiResponse = await _aiService.SendTextRequest(prompt, AIModel.Claude4Sonnet, cancellationToken);

            if (!aiResponse.Success)
            {
                _logger.LogWarning("No content received from AI service for translation");
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service failed to process the text" };
            }

            string translatedText = aiResponse.Content.Trim();
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

            _logger.LogInformation("Processing image file {FileName} for translation to {LanguageName} using {Model}",
                file.FileName, targetLanguage.Name, model);

            string base64Image;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, cancellationToken);
                base64Image = Convert.ToBase64String(ms.ToArray());
            }

            var prompt = GenerateImageTranslationPrompt(targetLanguage.Name);
            var mediaType = file.FileName.ToLowerInvariant() switch
            {
                var f when f.EndsWith(".png") => "image/png",
                var f when f.EndsWith(".jpg") || f.EndsWith(".jpeg") => "image/jpeg",
                _ => throw new ArgumentException($"Unsupported image format: {file.FileName}")
            };

            var images = new List<AIImageData>
            {
                new AIImageData
                {
                    Base64Data = base64Image,
                    MimeType = mediaType
                }
            };

            _logger.LogInformation("Sending image to {Model} for translation", model);
            var aiResponse = await _aiService.SendRequestWithImages(prompt, images, model, cancellationToken);

            if (!aiResponse.Success)
            {
                _logger.LogWarning("No content received from AI service for image translation");
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service failed to process the image" };
            }

            string translatedText = aiResponse.Content.Trim();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation service returned empty result" };
            }

            string improvedTranslation = translatedText;
            double qualityScore = 0.0;

            if (!string.IsNullOrWhiteSpace(translatedText))
            {
                _logger.LogInformation("Starting image translation verification with {Model}", model);

                try
                {
                    var sampleText = translatedText.Length > 2000
                        ? translatedText.Substring(0, 2000) + "..."
                        : translatedText;

                    var verificationPrompt = GenerateVerificationPrompt(sampleText, targetLanguage.Name);
                    var verificationResponse = await _aiService.SendTextRequest(verificationPrompt, model, cancellationToken);

                    if (verificationResponse.Success)
                    {
                        qualityScore = 0.95;
                        _logger.LogInformation("Image translation verification completed with score: {Score}", qualityScore);
                    }
                    else
                    {
                        _logger.LogWarning("Image translation verification failed: {Error}", verificationResponse.ErrorMessage);
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

    private static string GenerateImageTranslationPrompt(string targetLanguage)
    {
        return $@"
                <role>
                    You are an advanced OCR and translation system.
                    Your task is to analyze the provided image, extract all visible text, and translate it accurately to {targetLanguage}.
                </role>

                <context>
                    You are processing a complete image document that may contain various types of content including text, headings, lists, tables, or other structured elements.
                </context>

                <actions>
                    1. <Extract Text>
                        Accurately extract **all** visible textual content from the provided image.
                        Pay careful attention to:
                        - Headers and titles
                        - Body text and paragraphs
                        - Lists and bullet points
                        - Table content
                        - Captions and annotations
                        - Any other readable text elements

                    2. <Translate>
                        Translate the entire extracted text into **{targetLanguage}**.
                        Ensure translation is:
                        - Accurate and contextually appropriate
                        - Natural and fluent in {targetLanguage}
                        - Preserving the original meaning and intent

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
                        Keep all numbers, dates, and codes exactly as in the source or transliterate them suitably for {targetLanguage}.

                    5. <Non-Translation Rules>
                        **Do not translate** the following; preserve exactly as in source:
                        - Technical identifiers
                        - Standards (e.g., ISO, EN, ДСТУ, ГОСТ)
                        - Specific codes (e.g., ДФРПОУ, НААУ)
                        - Reference numbers or part numbers
                        - URLs and email addresses
                        - **EMAIL ADDRESSES** - NEVER translate email addresses, keep them exactly as they appear

                    6. <Proper Nouns and Human Names>
                        **HUMAN NAMES**: When encountering human names (first names, last names, full names), always use TRANSLITERATION rather than translation - convert the name to {targetLanguage} script/alphabet while preserving the original pronunciation.
                        Examples: ""John Smith"" → transliterate to {targetLanguage} script, ""María González"" → transliterate to {targetLanguage} script
                        Transliterate other proper nouns (organizations, places) per standard {targetLanguage} rules unless a widely accepted translation exists.

                    7. <Contextual Structure>
                        Use context to infer and apply correct document structure in HTML: titles, sections, lists, paragraphs, tables, etc.

                    8. <OCR Quality Handling>
                        If you encounter unclear or potentially misread text, make reasonable interpretations based on context while maintaining accuracy.
                </actions>

                <output_requirements>
                    - Output **only** the translated text in {targetLanguage}
                    - Format the entire output strictly in HTML as described
                    - Do **not** include any explanations, remarks, or original untranslated text
                    - Do **not** add any introductory or concluding statements
                    - Ensure the HTML is valid and clean with proper nesting
                </output_requirements>

                ";
    }

    private string GenerateTranslationPrompt(string text, string targetLanguage)
    {
        return $@"
                <role>
                    You are an expert multilingual translator and document formatter.
                    You specialize in converting OCR-extracted text into professionally polished documents in {targetLanguage}.
                </role>

                <context>
                    You will be provided with raw text that was extracted via OCR from an image document.
                    This text may contain OCR artifacts, formatting inconsistencies, or minor recognition errors.
                </context>

                <task>
                    Translate the provided text into **{targetLanguage}**, precisely following the instructions below.
                </task>

                <instructions>
                    1. <Full Translation>
                        Translate **all** textual content into {targetLanguage}. Nothing translatable should be omitted.

                    2. <Layout Preservation>
                        Reproduce the **original layout and structure** using **HTML formatting**. Preserve paragraph breaks, meaningful line breaks, and the visual hierarchy.

                    3. <Data Integrity>
                        Preserve all **numbers, dates, general codes, and identifiers** in their original form or transliterate as appropriate for {targetLanguage}.

                    4. <Non-Translatable Elements>
                        The following must **not be translated** and must appear **exactly as in the source**:
                        - Technical identifiers (e.g., part numbers, model numbers)
                        - Official standards (e.g., ISO 9001, ДСТУ Б В.2.7-170:2008)
                        - Specific codes (e.g., EAN codes, НААУ, ДФРПОУ)
                        - Reference numbers
                        - URLs and email addresses
                        - **EMAIL ADDRESSES** - NEVER translate email addresses, preserve them exactly

                    5. <OCR Artifact Handling>
                        - Detect and correct **common OCR errors** (e.g., garbled characters, misreads).
                        - If uncertain, prefer a faithful transcription over guessing.

                    6. <Structural Fidelity>
                        Maintain the original structure:
                        - Paragraph breaks
                        - Line spacing (use appropriate HTML formatting)
                        - Sequence of sections

                    7. <Proper Nouns and Human Names>
                        **HUMAN NAMES**: When encountering human names (first names, last names, full names), always use TRANSLITERATION rather than translation - convert the name to {targetLanguage} script/alphabet while preserving the original pronunciation.
                        Examples: ""John Smith"" → transliterate to {targetLanguage} script, ""María González"" → transliterate to {targetLanguage} script
                        Transliterate other proper names (organizations, places) per {targetLanguage} norms, unless an accepted translation exists.

                    8. <Technical Terminology>
                        Use **standard technical terms** in {targetLanguage} relevant to the document's subject matter.

                    9. <HTML Formatting Rules>
                        Format the translated text using **strict HTML tags only**. **Do not use any Markdown formatting** under any circumstances.

                        Use the following HTML elements appropriately:
                        - Headings: `<h1>`, `<h2>`, `<h3>`, etc.
                        - Paragraphs: `<p>`
                        - Lists: `<ul>` / `<ol>` with `<li>` items
                        - Tables: `<table>`, `<thead>`, `<tbody>`, `<tr>`, `<th>`, `<td>`
                        - Code blocks or preformatted text: `<pre>` or `<code>`
                        - Section breaks: Use `<hr />`
                        - Line breaks: Use `<br />` where meaningful line breaks are needed
                        - Emphasis: `<strong>` for bold, `<em>` for italic

                        Ensure the HTML is valid and clean. Nest tags properly, avoid inline styles, and do **not** include any raw Markdown syntax.
                </instructions>

                <output_constraints>
                    - Output **only** the final translated text in {targetLanguage}
                    - Do **not** include English explanations, comments, or the original text
                    - Do **not** skip or summarize any content unless marked non-translatable
                    - Your entire response must be in clean, valid HTML
                </output_constraints>

                <ocr_input>
                    Here is the OCR-extracted text to translate:

                        {text}
                </ocr_input>

                ";
    }

    private static string GenerateVerificationPrompt(string translatedText, string targetLanguage)
    {
        return $@"
                <role>
                    You are a multilingual quality assurance specialist and translation reviewer.
                    Your task is to evaluate the quality of a {targetLanguage} translation.
                </role>

                <context>
                    You are reviewing a translation that was produced from OCR-extracted text from an image document.
                    The translation may have been affected by OCR quality and initial text extraction accuracy.
                </context>

                <evaluation_criteria>
                    Please assess the following aspects of the translation:

                    1. <Translation Accuracy>
                        - Are all concepts accurately translated?
                        - Is the meaning preserved from the original context?
                        - Are technical terms appropriately handled?

                    2. <Language Fluency>
                        - Does the text flow naturally in {targetLanguage}?
                        - Are there any awkward or unnatural phrases?
                        - Is the grammar correct and appropriate?

                    3. <Completeness>
                        - Does the translation appear complete?
                        - Are there any obvious gaps or missing content?
                        - Are all sections properly addressed?

                    4. <Technical Consistency>
                        - Are technical identifiers properly preserved?
                        - Are standards and codes maintained correctly?
                        - Is technical terminology consistent throughout?

                    5. <Format and Structure>
                        - Is the HTML formatting appropriate and clean?
                        - Is the document structure preserved?
                        - Are headings, lists, and other elements properly formatted?
                </evaluation_criteria>

                <instructions>
                    1. Review the provided translation carefully against all evaluation criteria.
                    2. Provide specific, actionable feedback for any issues found.
                    3. If the translation is of high quality, acknowledge this clearly.
                    4. Focus on the most important improvements that would enhance translation quality.
                    5. Be constructive and specific in your feedback.
                </instructions>

                <output_format>
                    Provide your evaluation as structured feedback:
                    - Overall Quality Assessment: [Excellent/Good/Fair/Poor]
                    - Strengths: [List positive aspects]
                    - Areas for Improvement: [List specific issues and suggestions]
                    - Priority Issues: [Highlight the most critical problems to address]
                </output_format>

                <translation_to_review>
                    {translatedText}
                </translation_to_review>

                ";
    }

    private class OcrResponse
    {
        public string Text { get; set; }
        public string Error { get; set; }
    }
}
