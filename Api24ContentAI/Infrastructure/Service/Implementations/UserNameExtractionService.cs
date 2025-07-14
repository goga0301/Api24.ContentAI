using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class UserNameExtractionService : IUserNameExtractionService
    {
        private readonly IGeminiService _geminiService;
        private readonly ILogger<UserNameExtractionService> _logger;
        
        private static readonly string[] SupportedExtensions = { ".pdf", ".docx", ".doc", ".txt", ".md", ".png", ".jpg", ".jpeg" };

        public UserNameExtractionService(IGeminiService geminiService, ILogger<UserNameExtractionService> logger)
        {
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsSupportedFileType(string fileExtension)
        {
            return SupportedExtensions.Contains(fileExtension?.ToLowerInvariant());
        }

        public async Task<UserNameExtractionResult> ExtractUserNamesFromFileAsync(IFormFile file, CancellationToken cancellationToken)
        {
            return await ExtractUserNamesFromFileAsync(file, "English", cancellationToken);
        }

        public async Task<UserNameExtractionResult> ExtractUserNamesFromFileAsync(IFormFile file, string language, CancellationToken cancellationToken)
        {
            try
            {
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                _logger.LogInformation("Starting user name extraction from {FileType} file: {FileName} for {Language} language", 
                    fileExtension, file.FileName, language);

                var textExtractionResult = await ExtractTextContentUsingAI(file, fileExtension, cancellationToken);
                
                if (!textExtractionResult.Success)
                {
                    return new UserNameExtractionResult
                    {
                        Success = false,
                        ErrorMessage = textExtractionResult.ErrorMessage,
                        FileType = fileExtension,
                        FileName = file.FileName,
                        ExtractionMethod = "AI Text Extraction Failed"
                    };
                }

                var names = await ExtractNamesFromTextUsingAI(textExtractionResult.Content, language, cancellationToken);

                var cleanedNames = CleanAndValidateNames(names);

                _logger.LogInformation("Successfully extracted {NameCount} user names from {FileName} using {Language} context", 
                    cleanedNames.Count, file.FileName, language);

                return new UserNameExtractionResult
                {
                    Success = true,
                    UserNames = cleanedNames,
                    FileType = fileExtension,
                    FileName = file.FileName,
                    ExtractedTextLength = textExtractionResult.Content?.Length ?? 0,
                    ExtractionMethod = $"AI-Powered Extraction ({language})"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract user names from file: {FileName} for language: {Language}", file.FileName, language);
                return new UserNameExtractionResult
                {
                    Success = false,
                    ErrorMessage = $"Extraction failed: {ex.Message}",
                    FileType = Path.GetExtension(file.FileName).ToLowerInvariant(),
                    FileName = file.FileName,
                    ExtractionMethod = "Error"
                };
            }
        }

        private async Task<TextExtractionResult> ExtractTextContentUsingAI(IFormFile file, string fileExtension, CancellationToken cancellationToken)
        {
            try
            {
                return fileExtension switch
                {
                    ".txt" or ".md" => await ExtractFromPlainTextFile(file, cancellationToken),
                    ".pdf" => await ExtractFromPdfUsingAI(file, cancellationToken),
                    ".docx" or ".doc" => await ExtractFromWordUsingAI(file, cancellationToken),
                    ".png" or ".jpg" or ".jpeg" => await ExtractFromImageUsingAI(file, cancellationToken),
                    _ => new TextExtractionResult { Success = false, ErrorMessage = $"Unsupported file type: {fileExtension}" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text content from {FileType}", fileExtension);
                return new TextExtractionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<TextExtractionResult> ExtractFromPlainTextFile(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync(cancellationToken);
                
                return new TextExtractionResult 
                { 
                    Success = true, 
                    Content = content 
                };
            }
            catch (Exception ex)
            {
                return new TextExtractionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<TextExtractionResult> ExtractFromPdfUsingAI(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var fileData = await ConvertFileToBase64(file);
                
                var prompt = CreateTextExtractionPrompt("PDF document", 
                    "This is a PDF document. Please extract ALL text content from this document, maintaining the original structure and formatting as much as possible.");

                var parts = new List<GeminiPart>
                {
                    new GeminiPart { Text = prompt },
                    new GeminiPart 
                    {
                        InlineData = new GeminiInlineData
                        {
                            MimeType = "application/pdf",
                            Data = fileData
                        }
                    }
                };

                var response = await _geminiService.SendRequestWithFile(parts, cancellationToken);
                
                var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new TextExtractionResult { Success = false, ErrorMessage = "Gemini 2.5 Flash failed to extract text from PDF" };
                }

                return new TextExtractionResult { Success = true, Content = content };
            }
            catch (Exception ex)
            {
                return new TextExtractionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

                private async Task<TextExtractionResult> ExtractFromWordUsingAI(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var fileData = await ConvertFileToBase64(file);
                
                var extractionPrompt = $@"Please analyze the uploaded Word document and extract all text content. 
                                        Focus on:
                                        1. All paragraphs and text blocks
                                        2. Headings and subheadings
                                        3. Lists and bullet points
                                        4. Table content
                                        5. Any signatures or names mentioned
                                        6. Contact information

                                        Return the extracted text in a clean, readable format while preserving the document structure.";

                var parts = new List<GeminiPart>
                {
                    new GeminiPart { Text = extractionPrompt },
                    new GeminiPart 
                    {
                        InlineData = new GeminiInlineData
                        {
                            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                            Data = fileData
                        }
                    }
                };

                var response = await _geminiService.SendRequestWithFile(parts, cancellationToken);
                
                var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new TextExtractionResult { Success = false, ErrorMessage = "Gemini 2.5 Flash failed to extract text from Word document" };
                }

                return new TextExtractionResult { Success = true, Content = content };
            }
            catch (Exception ex)
            {
                return new TextExtractionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<TextExtractionResult> ExtractFromImageUsingAI(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var fileData = await ConvertFileToBase64(file);
                
                var prompt = CreateTextExtractionPrompt("image with text", 
                    "This is an image that may contain text. Please extract ALL visible text from this image, including any names, signatures, or other textual content.");

                var parts = new List<GeminiPart>
                {
                    new GeminiPart { Text = prompt },
                    new GeminiPart 
                    {
                        InlineData = new GeminiInlineData
                        {
                            MimeType = GetImageMimeType(file.FileName),
                            Data = fileData
                        }
                    }
                };

                var response = await _geminiService.SendRequestWithFile(parts, cancellationToken);
                
                var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new TextExtractionResult { Success = false, ErrorMessage = "Gemini 2.5 Flash failed to extract text from image" };
                }

                return new TextExtractionResult { Success = true, Content = content };
            }
            catch (Exception ex)
            {
                return new TextExtractionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private async Task<List<string>> ExtractNamesFromTextUsingAI(string textContent, string language, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(textContent))
                    return new List<string>();

                var limitedText = textContent.Length > 6000 
                    ? textContent.Substring(0, 6000) + "..." 
                    : textContent;

                var nameExtractionPrompt = CreateNameExtractionPrompt(limitedText, language);

                var geminiRequest = new GeminiRequest(nameExtractionPrompt);
                var response = await _geminiService.SendRequest(geminiRequest, cancellationToken);
                
                var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning("Gemini 2.5 Flash name extraction returned no results");
                    return new List<string>();
                }

                var names = ParseNamesFromAIResponse(content);
                
                _logger.LogDebug("Gemini 2.5 Flash extracted {NameCount} potential names", names.Count);
                return names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract names using Gemini 2.5 Flash");
                return new List<string>();
            }
        }

        private string CreateTextExtractionPrompt(string documentType, string specificInstructions)
        {
            return $@"You are an expert document text extraction specialist. 
                Your task is to extract ALL text content from the provided {documentType}.

                INSTRUCTIONS:
                {specificInstructions}

                EXTRACTION REQUIREMENTS:
                1. Extract every piece of readable text
                2. Maintain document structure and hierarchy
                3. Include headers, footers, and page content
                4. Preserve names, signatures, and contact information
                5. Include table contents and list items
                6. Maintain paragraph breaks and formatting cues

                OUTPUT FORMAT:
                - Return only the extracted text content
                - Use clear paragraph breaks
                - Maintain logical text flow
                - Do not add explanations or comments
                - Focus on accuracy and completeness

                Please extract the text content now:";
        }

        private string CreateNameExtractionPrompt(string textContent, string language)
        {
            return $@"You are an expert name recognition specialist.
                Your task is to identify and extract ALL human names from the provided text.

                LANGUAGE CONTEXT:
                The given text is primarily in {language}. Please focus on extracting names that are commonly used in {language}-speaking countries and cultures. Consider:
                - Traditional naming patterns and conventions for {language} culture
                - Common first names, surnames, and family names in {language}-speaking regions
                - Cultural titles and honorifics used in {language} contexts
                - Proper transliteration and spelling variations for {language} names

                IDENTIFICATION CRITERIA:
                ✓ INCLUDE these types of names:
                - Full names (First Last): ""John Smith"", ""Maria Garcia"", ""李明"", ""محمد علي""
                  - Names with titles: ""Dr. Sarah Johnson"", ""Mr. Robert Davis"", ""Prof. Zhang Wei""
                  - Names with middle names/initials: ""John M. Smith"", ""Mary Jane Watson""
                  - Professional signatures: ""Signed by: Michael Brown""
                  - Author attributions: ""Written by Emma Wilson""
                  - Contact names in directories or lists
                  - Names with cultural prefixes/suffixes common in {language} regions

                  ✗ EXCLUDE these items:
                  - Company/business names: ""Apple Inc."", ""Microsoft Corporation""
                  - Place names: ""New York"", ""London Bridge""
                  - Product names: ""iPhone"", ""Windows""
                  - Book/movie titles: ""Harry Potter"", ""Star Wars""
                  - Single first names only: ""John"" (unless clearly a person in context)
                  - Generic titles without names: ""The Manager"", ""Customer Service""

                  EXTRACTION RULES:
                  1. Focus on human personal names only, especially those common in {language} culture
                  2. Include both formal and informal name formats typical for {language} speakers
                  3. Capture names from signatures, contact lists, author credits
                  4. Look for names in addresses, email signatures, letterheads
                  5. Include names from employee lists, directories, rosters
                  6. Preserve original spelling and character sets (Latin, Cyrillic, Arabic, Chinese, etc.)
                  7. Consider {language}-specific naming conventions and structures

                  OUTPUT FORMAT:
                  Return ONLY a valid JSON array of names. No explanations, no additional text, just the JSON array:

                  {{
                       ""names"": [
                           ""John Smith"",
                           ""Dr. Sarah Johnson"",
                           ""Maria Elena Rodriguez""
                       ]
                   }}

        If no names are found, return:
        {{
             ""names"": []
         }}

        TEXT TO ANALYZE (in {language}):
        {textContent}

        Extract the names now as JSON, focusing on {language} naming patterns:";
    }

    private List<string> ParseNamesFromAIResponse(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
            return new List<string>();

        try
        {
            var cleanedResponse = aiResponse.Trim();
            
            var startIndex = cleanedResponse.IndexOf('{');
            var lastIndex = cleanedResponse.LastIndexOf('}');
            
            if (startIndex >= 0 && lastIndex > startIndex)
            {
                var jsonString = cleanedResponse.Substring(startIndex, lastIndex - startIndex + 1);
                
                var jsonDocument = JsonDocument.Parse(jsonString);
                var root = jsonDocument.RootElement;
                
                if (root.TryGetProperty("names", out var namesProperty) && namesProperty.ValueKind == JsonValueKind.Array)
                {
                    var names = new List<string>();
                    foreach (var nameElement in namesProperty.EnumerateArray())
                    {
                        if (nameElement.ValueKind == JsonValueKind.String)
                        {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                names.Add(name.Trim());
                            }
                        }
                    }
                    return names;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response from Gemini, falling back to line-by-line parsing. Response: {Response}", aiResponse);
        }
        
        var fallbackNames = aiResponse
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("Here") && !line.StartsWith("The following") && !line.StartsWith("Names found"))
            .Where(line => !line.StartsWith("{") && !line.StartsWith("}") && !line.Contains("\"names\""))
            .Select(line => line.Trim('.', '-', '*', '•', '"', ','))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return fallbackNames;
    }

    private List<string> CleanAndValidateNames(List<string> names)
    {
        return names
            .Select(CleanName)
            .Where(IsValidName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();
    }

    private string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = Regex.Replace(name, @"^(Mr\.?|Mrs\.?|Ms\.?|Dr\.?|Prof\.?|Sir|Madam)\s+", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+(Jr\.?|Sr\.?|III|IV|PhD|MD|Esq\.?)$", "", RegexOptions.IgnoreCase);
        
        name = Regex.Replace(name, @"\s+", " ").Trim();
        name = name.Trim(',', '.', ';', ':', '-', '_');

        if (!string.IsNullOrWhiteSpace(name))
        {
            var words = name.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(words[i]) && words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            name = string.Join(' ', words);
        }

        return name;
    }

    private bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            return false;

        if (!name.Contains(' '))
            return false;

        if (name.Length > 60)
            return false;

        if (Regex.IsMatch(name, @"[^a-zA-Z\s\.\-']"))
            return false;

        var invalidPatterns = new[]
        {
            @"\b(Inc|LLC|Ltd|Corp|Company|Organization|Department|Office|Page|Document|File|Email|Phone|Address)\b",
            @"\b(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b",
            @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\b",
            @"\b(Street|Avenue|Road|Boulevard|Drive|Lane|Court|Place)\b"
        };

        foreach (var pattern in invalidPatterns)
        {
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                return false;
        }

        return true;
    }

    private async Task<string> ConvertFileToBase64(IFormFile file)
    {
        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private string GetImageMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "image/jpeg"
        };
    }

    private class TextExtractionResult
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string ErrorMessage { get; set; }
    }
}
} 
