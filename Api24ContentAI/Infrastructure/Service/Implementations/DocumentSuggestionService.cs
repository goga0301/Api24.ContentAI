using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentSuggestionService : IDocumentSuggestionService
    {
        private readonly IClaudeService _claudeService;
        private readonly ILanguageService _languageService;
        private readonly ILogger<DocumentSuggestionService> _logger;

        public DocumentSuggestionService(
            IClaudeService claudeService,
            ILanguageService languageService,
            ILogger<DocumentSuggestionService> logger)
        {
            _claudeService = claudeService;
            _languageService = languageService;
            _logger = logger;
        }

        public async Task<List<TranslationSuggestion>> GenerateSuggestions(
            string originalContent,
            string translatedContent,
            int targetLanguageId,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Generating suggestions for translation to language ID: {LanguageId}", targetLanguageId);

                var language = await _languageService.GetById(targetLanguageId, cancellationToken);
                if (language == null)
                {
                    _logger.LogWarning("Language not found: {LanguageId}", targetLanguageId);
                    return new List<TranslationSuggestion>();
                }

                var prompt = GenerateTranslationReviewPrompt(originalContent, translatedContent, language.Name);

                var claudeRequest = new ClaudeRequest(prompt);
                var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

                var responseText = claudeResponse.Content?.FirstOrDefault()?.Text;
                if (string.IsNullOrEmpty(responseText))
                {
                    _logger.LogWarning("Empty response from Claude for suggestions");
                    return new List<TranslationSuggestion>();
                }

                // Extract JSON from the response
                var jsonStart = responseText.IndexOf('[');
                var jsonEnd = responseText.LastIndexOf(']');

                if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                {
                    _logger.LogWarning("No valid JSON array found in Claude response");
                    return GenerateFallbackSuggestions();
                }

                var jsonContent = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var suggestions = JsonSerializer.Deserialize<List<JsonSuggestion>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return suggestions?.Select(s => new TranslationSuggestion
                {
                    Title = s.Title ?? "Improvement Suggestion",
                    Description = s.Description ?? "Consider this improvement",
                    Type = ParseSuggestionType(s.Type),
                    OriginalText = s.OriginalText ?? "",
                    SuggestedText = s.SuggestedText ?? "",
                    Priority = Math.Max(1, Math.Min(3, s.Priority))
                }).ToList() ?? new List<TranslationSuggestion>();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating suggestions");
                return GenerateFallbackSuggestions();
            }
        }

        public async Task<ApplySuggestionResponse> ApplySuggestion(
            ApplySuggestionRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Applying suggestion: {SuggestionId}", request.SuggestionId);

                var language = await _languageService.GetById(request.TargetLanguageId, cancellationToken);
                if (language == null)
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Target language not found"
                    };
                }

                var prompt = GenerateApplySuggestionPrompt(
                    request.TranslatedContent, 
                    request.Suggestion, 
                    language.Name);

                var claudeRequest = new ClaudeRequest(prompt);
                var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

                var updatedContent = claudeResponse.Content?.FirstOrDefault()?.Text?.Trim();
                if (string.IsNullOrEmpty(updatedContent))
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to generate updated content"
                    };
                }

                // Generate new suggestions for the updated content
                var newSuggestions = await GenerateSuggestions("", updatedContent, request.TargetLanguageId, cancellationToken);

                return new ApplySuggestionResponse
                {
                    Success = true,
                    UpdatedContent = updatedContent,
                    ChangeDescription = $"Applied {request.Suggestion.Type} suggestion: {request.Suggestion.Title}",
                    NewSuggestions = newSuggestions
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying suggestion");
                return new ApplySuggestionResponse
                {
                    Success = false,
                    ErrorMessage = $"Failed to apply suggestion: {ex.Message}"
                };
            }
        }

        private static SuggestionType ParseSuggestionType(string type)
        {
            return type?.ToLower() switch
            {
                "grammarerror" => SuggestionType.GrammarError,
                "syntaxerror" => SuggestionType.SyntaxError,
                "styleimprovement" => SuggestionType.StyleImprovement,
                "terminology" => SuggestionType.Terminology,
                "punctuation" => SuggestionType.Punctuation,
                "formatting" => SuggestionType.Formatting,
                "clarity" => SuggestionType.Clarity,
                "consistency" => SuggestionType.Consistency,
                _ => SuggestionType.StyleImprovement
            };
        }

        private static string GenerateTranslationReviewPrompt(string originalContent, string translatedContent, string targetLanguage)
        {
            return $@"You are an expert professional translation quality reviewer and linguist with deep expertise in {targetLanguage} language and cross-cultural communication. Your task is to meticulously analyze the provided translation and identify exactly 4 specific, actionable improvement suggestions.

                ## CONTENT TO ANALYZE:

                **Original Source Text:**
                {originalContent}

                **Translation to {targetLanguage}:**
                {translatedContent}

                ## YOUR ANALYSIS TASK:

                Conduct a comprehensive quality assessment focusing on these critical areas:

                ### 1. LINGUISTIC ACCURACY
                - Grammar correctness and proper sentence structure
                - Verb tenses, subject-verb agreement, and syntactic patterns
                - Proper use of articles, prepositions, and conjunctions
                - Spelling, capitalization, and orthographic conventions

                ### 2. SEMANTIC FIDELITY  
                - Accurate meaning transfer without loss or distortion
                - Proper handling of idiomatic expressions and cultural references
                - Contextually appropriate word choices and register
                - Preservation of source text tone and intent

                ### 3. STYLISTIC QUALITY
                - Natural flow and readability in the target language
                - Appropriate formality level and register consistency
                - Sentence variety and rhythm that sounds native
                - Cultural appropriateness and localization

                ### 4. TECHNICAL PRECISION
                - Terminology consistency and domain-specific accuracy
                - Proper formatting, punctuation, and special characters
                - Handling of numbers, dates, names, and technical terms
                - Document structure and markdown formatting preservation

                ## OUTPUT REQUIREMENTS:

                Provide your analysis as a valid JSON array containing exactly 4 suggestion objects. Each suggestion must include:

                ```json
                [
                {{
                    ""title"": ""Concise, specific title (max 50 characters)"",
                    ""description"": ""Detailed explanation of the issue and why the improvement is needed (100-200 characters)"",
                    ""type"": ""One of: GrammarError, SyntaxError, StyleImprovement, Terminology, Punctuation, Formatting, Clarity, Consistency"",
                    ""originalText"": ""Exact text segment from the translation that needs improvement"",
                    ""suggestedText"": ""Your improved version that replaces the original text"",
                    ""priority"": 1-3 (1=Critical/High impact, 2=Moderate impact, 3=Minor/Polish)
                }}
                ]
                ```

                ## ANALYSIS GUIDELINES:

                ✅ **DO:**
                - Focus on the most impactful improvements that enhance translation quality
                - Provide specific, actionable suggestions with exact text segments
                - Consider cultural nuances and target audience expectations
                - Prioritize improvements that affect meaning accuracy and naturalness
                - Ensure suggested improvements are grammatically correct and contextually appropriate

                ❌ **AVOID:**
                - Vague or general suggestions without specific text examples
                - Changes that alter the original meaning or intent
                - Overly minor cosmetic changes unless they affect readability
                - Suggestions that make the text less natural in the target language

                Even if the translation appears to be of high quality, identify areas for enhancement that would make it more natural, accurate, or culturally appropriate for native {targetLanguage} speakers.

                Return only the JSON array with exactly 4 suggestions - no additional text or explanations outside the JSON structure.";
        }

        private static string GenerateApplySuggestionPrompt(string translatedContent, TranslationSuggestion suggestion, string targetLanguage)
        {
            return $@"You are an expert professional translator and linguistic editor with specialized expertise in {targetLanguage}. Your task is to carefully apply a specific improvement suggestion to enhance the quality of a translated text while maintaining its integrity, meaning, and natural flow.

                    ## CURRENT TRANSLATED TEXT ({targetLanguage}):

                    {translatedContent}

                    ## IMPROVEMENT SUGGESTION TO APPLY:

                    **Suggestion Type:** {suggestion.Type}
                    **Issue Title:** {suggestion.Title}
                    **Detailed Description:** {suggestion.Description}
                    **Priority Level:** {suggestion.Priority} (1=Critical, 2=Moderate, 3=Minor)

                    **Target Text to Improve:**
                    ""{suggestion.OriginalText}""

                    **Suggested Replacement:**
                    ""{suggestion.SuggestedText}""

                    ## YOUR EDITING TASK:

                    ### PRIMARY OBJECTIVES:
                    1. **Locate and Replace:** Find the exact or semantically equivalent text segment that matches the ""Target Text to Improve""
                    2. **Apply Enhancement:** Replace it with the ""Suggested Replacement"" or adapt the improvement concept appropriately
                    3. **Maintain Coherence:** Ensure the entire text flows naturally and cohesively after the modification
                    4. **Preserve Integrity:** Keep all formatting, structure, and non-target content exactly as provided

                    ### DETAILED INSTRUCTIONS:

                    #### If Exact Text Match Found:
                    - Replace the exact text segment with the suggested improvement
                    - Adjust surrounding grammar/syntax if needed for smooth integration
                    - Ensure verb tenses, articles, and agreement patterns remain consistent

                    #### If Exact Text Not Found:
                    - Identify the most semantically similar text segment
                    - Apply the improvement concept to that section
                    - Maintain the same type of enhancement (grammar, style, terminology, etc.)

                    #### Formatting Preservation:
                    - **Markdown:** Preserve all markdown formatting (headers, lists, links, emphasis)
                    - **Punctuation:** Maintain consistent punctuation patterns
                    - **Spacing:** Keep original paragraph breaks and line spacing
                    - **Special Characters:** Preserve any special characters, symbols, or formatting codes

                    #### Quality Assurance:
                    - Ensure the improvement actually enhances the text quality
                    - Verify that the change doesn't introduce new errors
                    - Confirm the modification aligns with {targetLanguage} linguistic conventions
                    - Check that the overall meaning and tone remain unchanged

                    ## OUTPUT REQUIREMENTS:

                    Return ONLY the complete improved translated text with the suggestion applied. Do not include:
                    - Explanations of what you changed
                    - Commentary on the improvement
                    - Bracketed notes or annotations
                    - Multiple versions or alternatives

                    The output should be the full, finalized text that seamlessly incorporates the suggested improvement while maintaining the original structure and quality of the translation.

                    ## QUALITY STANDARDS:

                    ✅ **Success Criteria:**
                    - Natural, fluent {targetLanguage} that sounds native
                    - Grammatically correct and stylistically appropriate
                    - Meaningful improvement over the original version
                    - Consistent formatting and structure
                    - Preserved semantic accuracy

                    ❌ **Avoid:**
                    - Introducing new grammatical errors
                    - Changing the original meaning or intent
                    - Removing or altering unrelated content
                    - Breaking markdown or other formatting
                    - Making the text sound less natural

                    Apply the suggestion thoughtfully and return the enhanced translated text.";
        }

        private static List<TranslationSuggestion> GenerateFallbackSuggestions()
        {
            return new List<TranslationSuggestion>
            {
                new()
                {
                    Title = "Review Translation",
                    Description = "Consider reviewing the translation for accuracy and naturalness",
                    Type = SuggestionType.StyleImprovement,
                    Priority = 2
                }
            };
        }

        private class JsonSuggestion
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string Type { get; set; }
            public string OriginalText { get; set; }
            public string SuggestedText { get; set; }
            public int Priority { get; set; }
        }
    }
}