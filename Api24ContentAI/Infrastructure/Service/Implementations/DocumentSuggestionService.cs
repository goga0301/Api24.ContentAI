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

                // Use a fresh cancellation token with timeout for database operations to avoid inherited cancellation
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                LanguageModel language;
                try
                {
                    language = await _languageService.GetById(targetLanguageId, combinedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("Database operation timed out while fetching language {LanguageId}, skipping suggestions", targetLanguageId);
                    return new List<TranslationSuggestion>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Suggestion generation was cancelled by user request");
                    return new List<TranslationSuggestion>();
                }

                if (language == null)
                {
                    _logger.LogWarning("Language not found: {LanguageId}, skipping suggestions", targetLanguageId);
                    return new List<TranslationSuggestion>();
                }

                // Use original cancellation token for Claude API call (should be fast)
                var prompt = GenerateTranslationReviewPrompt(originalContent, translatedContent, language.Name);

                var claudeRequest = new ClaudeRequest(prompt);
                
                ClaudeResponse claudeResponse;
                try
                {
                    // Use a timeout for Claude API call as well
                    using var claudeTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    using var claudeCombinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, claudeTimeoutCts.Token);
                    
                    claudeResponse = await _claudeService.SendRequest(claudeRequest, claudeCombinedCts.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Claude API call was cancelled by user request");
                    return GenerateFallbackSuggestions();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Claude API call timed out, returning fallback suggestions");
                    return GenerateFallbackSuggestions();
                }

                var responseText = claudeResponse.Content?.FirstOrDefault()?.Text;
                if (string.IsNullOrEmpty(responseText))
                {
                    _logger.LogWarning("Empty response from Claude for suggestions");
                    return GenerateFallbackSuggestions();
                }

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

                var result = suggestions?.Select(s => new TranslationSuggestion
                {
                    Title = s.Title ?? "Improvement Suggestion",
                    Description = s.Description ?? "Consider this improvement",
                    Type = ParseSuggestionType(s.Type),
                    OriginalText = s.OriginalText ?? "",
                    SuggestedText = s.SuggestedText ?? "",
                    Priority = Math.Max(1, Math.Min(3, s.Priority))
                }).ToList() ?? new List<TranslationSuggestion>();

                _logger.LogInformation("Successfully generated {Count} suggestions", result.Count);
                return result;

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

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                LanguageModel language;
                try
                {
                    language = await _languageService.GetById(request.TargetLanguageId, combinedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("Database operation timed out while fetching language {LanguageId}", request.TargetLanguageId);
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Database timeout while fetching language information"
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Operation was cancelled by user request"
                    };
                }

                if (language == null)
                {
                    _logger.LogWarning("Language not found: {LanguageId}", request.TargetLanguageId);
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Target language not found"
                    };
                }

                var effectiveSuggestion = new TranslationSuggestion
                {
                    Id = request.Suggestion.Id,
                    Title = request.Suggestion.Title,
                    Description = request.Suggestion.Description,
                    Type = request.Suggestion.Type,
                    OriginalText = request.EditedOriginalText ?? request.Suggestion.OriginalText,
                    SuggestedText = request.EditedSuggestedText ?? request.Suggestion.SuggestedText,
                    Priority = request.Suggestion.Priority
                };

                _logger.LogInformation("Using effective suggestion - Title: {Title}, Type: {Type}, OriginalText: {OriginalText}, SuggestedText: {SuggestedText}", 
                    effectiveSuggestion.Title, effectiveSuggestion.Type, effectiveSuggestion.OriginalText, effectiveSuggestion.SuggestedText);
                
                _logger.LogInformation("Request details - HasEdits: {HasEdits}, EditedOriginalText: '{EditedOriginalText}', EditedSuggestedText: '{EditedSuggestedText}'", 
                    request.HasEdits, request.EditedOriginalText ?? "null", request.EditedSuggestedText ?? "null");

                var prompt = GenerateApplySuggestionPrompt(
                    request.TranslatedContent, 
                    effectiveSuggestion, 
                    language.Name);

                _logger.LogDebug("Sending prompt to Claude with SuggestedText: '{SuggestedText}'", effectiveSuggestion.SuggestedText);

                var claudeRequest = new ClaudeRequest(prompt);
                
                ClaudeResponse claudeResponse;
                try
                {
                    using var claudeTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    using var claudeCombinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, claudeTimeoutCts.Token);
                    
                    claudeResponse = await _claudeService.SendRequest(claudeRequest, claudeCombinedCts.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Operation was cancelled by user request"
                    };
                }
                catch (OperationCanceledException)
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Claude API call timed out"
                    };
                }

                var updatedContent = claudeResponse.Content?.FirstOrDefault()?.Text?.Trim();
                if (string.IsNullOrEmpty(updatedContent))
                {
                    return new ApplySuggestionResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to generate updated content"
                    };
                }

                using var newSuggestionsCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                List<TranslationSuggestion> newSuggestions;
                try
                {
                    newSuggestions = await GenerateSuggestions("", updatedContent, request.TargetLanguageId, newSuggestionsCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("New suggestions generation timed out, returning without new suggestions");
                    newSuggestions = new List<TranslationSuggestion>();
                }

                var changeDescription = request.HasEdits 
                    ? $"Applied edited suggestion: {effectiveSuggestion.Title}"
                    : $"Applied suggestion: {effectiveSuggestion.Title}";

                return new ApplySuggestionResponse
                {
                    Success = true,
                    UpdatedContent = updatedContent,
                    ChangeDescription = changeDescription,
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
            return $@"
            <role>
                You are an expert professional translation quality reviewer and linguist with deep expertise in {targetLanguage} language and cross-cultural communication. Your task is to meticulously analyze the provided translation and identify exactly 4 specific, actionable improvement suggestions.
            </role>

            <content_to_analyze>
                <original_source>
                {originalContent}
                </original_source>

                    <translation_to_review>
                    {translatedContent}
                </translation_to_review>
            </content_to_analyze>

            <analysis_task>
                Conduct a comprehensive quality assessment focusing on these critical areas:

                <focus_areas>
                    <linguistic_accuracy>
                        - Grammar correctness and proper sentence structure
                        - Verb tenses, subject-verb agreement, and syntactic patterns
                        - Proper use of articles, prepositions, and conjunctions
                        - Spelling, capitalization, and orthographic conventions
                    </linguistic_accuracy>

                    <semantic_fidelity>
                        - Accurate meaning transfer without loss or distortion
                        - Proper handling of idiomatic expressions and cultural references
                        - Contextually appropriate word choices and register
                        - Preservation of source text tone and intent
                    </semantic_fidelity>

                    <stylistic_quality>
                        - Natural flow and readability in the target language
                        - Appropriate formality level and register consistency
                        - Sentence variety and rhythm that sounds native
                        - Cultural appropriateness and localization
                    </stylistic_quality>

                    <technical_precision>
                        - Terminology consistency and domain-specific accuracy
                        - Proper formatting, punctuation, and special characters
                        - Handling of numbers, dates, names, and technical terms
                        - Document structure and markdown formatting preservation
                    </technical_precision>
                </focus_areas>
            </analysis_task>

                <output_requirements>
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
                    </output_requirements>

                <guidelines>
                    <do>
                    - Focus on the most impactful improvements that enhance translation quality
                    - Provide specific, actionable suggestions with exact text segments
                    - Consider cultural nuances and target audience expectations
                    - Prioritize improvements that affect meaning accuracy and naturalness
                    - Ensure suggested improvements are grammatically correct and contextually appropriate
                    </do>

                    <avoid>
                    - Vague or general suggestions without specific text examples
                    - Changes that alter the original meaning or intent
                    - Overly minor cosmetic changes unless they affect readability
                    - Suggestions that make the text less natural in the target language
                    </avoid>
                </guidelines>

                    <instructions>
                    Even if the translation appears to be of high quality, identify areas for enhancement that would make it more natural, accurate, or culturally appropriate for native {targetLanguage} speakers.

                    Return only the JSON array with exactly 4 suggestions - no additional text or explanations outside the JSON structure.
                    </instructions>";
        }

        private static string GenerateApplySuggestionPrompt(string translatedContent, TranslationSuggestion suggestion, string targetLanguage)
        {
            return $@"
                <role>
                    You are an expert professional translator and linguistic editor with specialized expertise in {targetLanguage}. Your task is to carefully apply a specific improvement suggestion to enhance the quality of a translated text while maintaining its integrity, meaning, and natural flow.
                </role>

                <current_content>
                    <translated_text language=""{targetLanguage}"">
                        {translatedContent}
                    </translated_text>
                </current_content>

                <improvement_suggestion>
                    <suggestion_type>{suggestion.Type}</suggestion_type>
                    <title>{suggestion.Title}</title>
                    <description>{suggestion.Description}</description>
                    <priority>{suggestion.Priority}</priority>

                    <target_text_to_improve>
                        {suggestion.OriginalText}
                    </target_text_to_improve>

                    <suggested_replacement>
                        {suggestion.SuggestedText}
                    </suggested_replacement>
                </improvement_suggestion>

                <critical_instruction>
                    You must use the ""Suggested Replacement"" text EXACTLY as provided, without any modifications, corrections, or improvements. Do not attempt to fix grammar, spelling, or stylistic issues in the replacement text - use it character-for-character as specified.
                </critical_instruction>

                <editing_task>
                    <primary_objectives>
                        1. Locate and Replace: Find the exact or semantically equivalent text segment that matches the ""Target Text to Improve""
                        2. Apply Enhancement: Replace it with the ""Suggested Replacement"" EXACTLY as provided
                        3. Maintain Coherence: Ensure the entire text flows naturally and cohesively after the modification
                        4. Preserve Integrity: Keep all formatting, structure, and non-target content exactly as provided
                    </primary_objectives>

                    <detailed_instructions>
                        <exact_match_found>
                            - Replace the exact text segment with the ""Suggested Replacement"" text EXACTLY as provided
                            - Do NOT modify, correct, or ""improve"" the suggested replacement text
                            - Use the replacement text character-for-character as specified
                            - Adjust surrounding grammar/syntax if needed for smooth integration
                            - Ensure verb tenses, articles, and agreement patterns remain consistent
                        </exact_match_found>

                        <exact_match_not_found>
                            - Identify the most semantically similar text segment
                            - Apply the improvement concept to that section
                            - Maintain the same type of enhancement (grammar, style, terminology, etc.)
                        </exact_match_not_found>

                        <formatting_preservation>
                            - Markdown: Preserve all markdown formatting (headers, lists, links, emphasis)
                            - Punctuation: Maintain consistent punctuation patterns
                            - Spacing: Keep original paragraph breaks and line spacing
                            - Special Characters: Preserve any special characters, symbols, or formatting codes
                        </formatting_preservation>

                        <quality_assurance>
                            - Ensure the improvement actually enhances the text quality
                            - Verify that the change doesn't introduce new errors
                            - Confirm the modification aligns with {targetLanguage} linguistic conventions
                            - Check that the overall meaning and tone remain unchanged
                        </quality_assurance>
                    </detailed_instructions>
                </editing_task>

                <output_requirements>
                    Return ONLY the complete improved translated text with the suggestion applied.

                    <do_not_include>
                        - Explanations of what you changed
                        - Commentary on the improvement
                        - Bracketed notes or annotations
                        - Multiple versions or alternatives
                    </do_not_include>

                    The output should be the full, finalized text that seamlessly incorporates the suggested improvement while maintaining the original structure and quality of the translation.
                </output_requirements>

                <quality_standards>
                    <success_criteria>
                        - Natural, fluent {targetLanguage} that sounds native
                        - Grammatically correct and stylistically appropriate
                        - Meaningful improvement over the original version
                        - Consistent formatting and structure
                        - Preserved semantic accuracy
                    </success_criteria>

                    <avoid>
                        - Introducing new grammatical errors
                        - Changing the original meaning or intent
                        - Removing or altering unrelated content
                        - Breaking markdown or other formatting
                        - Making the text sound less natural
                    </avoid>
                </quality_standards>

                <final_instruction>
                    Apply the suggestion thoughtfully and return the enhanced translated text.
                </final_instruction>";
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
