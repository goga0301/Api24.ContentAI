using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentSuggestionService : IDocumentSuggestionService
    {
        private readonly IAIService _aiService;
        private readonly IClaudeService _claudeService;
        private readonly ILanguageService _languageService;
        private readonly ILogger<DocumentSuggestionService> _logger;

        public DocumentSuggestionService(
            IAIService aiService,
            IClaudeService claudeService,
            ILanguageService languageService,
            ILogger<DocumentSuggestionService> logger)
        {
            _aiService = aiService;
            _claudeService = claudeService;
            _languageService = languageService;
            _logger = logger;
        }

        public async Task<List<TranslationSuggestion>> GenerateSuggestions(
            string originalContent,
            string translatedContent,
            int targetLanguageId,
            int outputLanguageId,
            CancellationToken cancellationToken,
            List<TranslationSuggestion> previousSuggestions = null,
            AIModel? model = null)
        {
            try
            {
                _logger.LogInformation("Generating suggestions for translation to language ID: {LanguageId} using model: {Model}", 
                    targetLanguageId, model?.ToString() ?? "Claude (default)");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                LanguageModel targetLanguage;
                try
                {
                    targetLanguage = await _languageService.GetById(targetLanguageId, combinedCts.Token);
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

                LanguageModel outputLanguage;
                try
                {
                    outputLanguage = await _languageService.GetById(outputLanguageId, combinedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("Database operation timed out while fetching language {LanguageId}, skipping suggestions", outputLanguageId);
                    return new List<TranslationSuggestion>();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Suggestion generation was cancelled by user request");
                    return new List<TranslationSuggestion>();
                }


                if (targetLanguage == null)
                {
                    _logger.LogWarning("Language not found: {LanguageId}, skipping suggestions", targetLanguageId);
                    return new List<TranslationSuggestion>();
                }

                if (outputLanguage == null) {
                    _logger.LogWarning("Language not found: {LanguageId}, skipping suggestions", targetLanguageId);
                    return new List<TranslationSuggestion>();

                }

                var prompt = GenerateTranslationReviewPrompt(originalContent, translatedContent, targetLanguage.Name, outputLanguage.Name, previousSuggestions);

                string responseText;
                if (model.HasValue)
                {
                    var aiResponse = await SendAIRequestWithTimeout(prompt, model.Value, cancellationToken);
                    if (!aiResponse.Success)
                    {
                        _logger.LogWarning("AI service failed for model {Model}: {Error}", model.Value, aiResponse.ErrorMessage);
                        return GenerateFallbackSuggestions();
                    }
                    responseText = aiResponse.Content;
                }
                else
                {
                    var claudeResponse = await SendClaudeRequestWithTimeout(prompt, cancellationToken);
                    if (claudeResponse == null)
                    {
                        return GenerateFallbackSuggestions();
                    }
                    responseText = claudeResponse.Content?.FirstOrDefault()?.Text;
                }

                if (string.IsNullOrEmpty(responseText))
                {
                    _logger.LogWarning("Empty response from AI service for suggestions");
                    return GenerateFallbackSuggestions();
                }

                // Debug logging to see what each AI model returns
                _logger.LogInformation("AI Response from {Model} (length: {Length}): {Response}", 
                    model?.ToString() ?? "Claude", responseText.Length, 
                    responseText.Length > 500 ? responseText.Substring(0, 500) + "..." : responseText);

                var jsonStart = responseText.IndexOf('[');
                var jsonEnd = responseText.LastIndexOf(']');

                if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                {
                    _logger.LogWarning("No valid JSON array found in AI response from {Model}. Response: {Response}", 
                        model?.ToString() ?? "Claude", responseText);
                    return GenerateFallbackSuggestions();
                }

                var jsonContent = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                _logger.LogInformation("Extracted JSON from {Model}: {JsonContent}", 
                    model?.ToString() ?? "Claude", jsonContent);

                var suggestions = JsonSerializer.Deserialize<List<JsonSuggestion>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Deserialized {Count} suggestions from {Model}", 
                    suggestions?.Count ?? 0, model?.ToString() ?? "Claude");

                var rawResult = suggestions?.Select(s => new TranslationSuggestion
                {
                    Title = s.Title ?? "Improvement Suggestion",
                    Description = s.Description ?? "Consider this improvement",
                    Type = ParseSuggestionType(s.Type),
                    OriginalText = s.OriginalText ?? "",
                    SuggestedText = s.SuggestedText ?? "",
                    Priority = Math.Max(1, Math.Min(3, s.Priority))
                }).ToList() ?? new List<TranslationSuggestion>();

                // Filter out duplicates and similar suggestions
                var filteredResult = FilterDuplicateSuggestions(rawResult, previousSuggestions ?? new List<TranslationSuggestion>());

                _logger.LogInformation("Generated {RawCount} suggestions, filtered to {FilteredCount} unique suggestions using {Model}", 
                    rawResult.Count, filteredResult.Count, model?.ToString() ?? "Claude");
                
                return filteredResult;

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

                var language = await GetLanguageOrNullAsync(request.TargetLanguageId, cancellationToken);
                if (language == null)
                {
                    _logger.LogWarning("Language not found or could not be fetched: {LanguageId}", request.TargetLanguageId);
                    return BuildFailureResponse("Target language not found or could not be fetched");
                }

                var effectiveSuggestion = new TranslationSuggestion
                {
                    Id = request.Suggestion.Id,
                    Title = request.Suggestion.Title,
                    Description = request.Suggestion.Description,
                    Type = request.Suggestion.Type,
                    OriginalText = (string.IsNullOrEmpty(request.EditedOriginalText) || request.EditedOriginalText == "string") 
                        ? request.Suggestion.OriginalText 
                        : request.EditedOriginalText,
                    SuggestedText = (string.IsNullOrEmpty(request.EditedSuggestedText) || request.EditedSuggestedText == "string") 
                        ? request.Suggestion.SuggestedText 
                        : request.EditedSuggestedText,
                    Priority = request.Suggestion.Priority
                };

                _logger.LogInformation(
                        "Using effective suggestion - Title: {Title}, Type: {Type}, OriginalText: {OriginalText}, SuggestedText: {SuggestedText}",
                        effectiveSuggestion.Title, effectiveSuggestion.Type, effectiveSuggestion.OriginalText, effectiveSuggestion.SuggestedText);

                _logger.LogInformation(
                        "Request details - HasEdits: {HasEdits}, EditedOriginalText: '{EditedOriginalText}', EditedSuggestedText: '{EditedSuggestedText}'",
                        request.HasEdits, request.EditedOriginalText ?? "null", request.EditedSuggestedText ?? "null");

                var updatedContent = ReplaceInHtml(request.TranslatedContent, effectiveSuggestion.OriginalText, effectiveSuggestion.SuggestedText);

                var changeDescription = request.HasEdits
                    ? $"Applied edited suggestion: {effectiveSuggestion.Title}"
                    : $"Applied suggestion: {effectiveSuggestion.Title}";

                return new ApplySuggestionResponse
                {
                    Success = true,
                    UpdatedContent = updatedContent,
                    ChangeDescription = changeDescription
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying suggestion");
                return BuildFailureResponse($"Failed to apply suggestion: {ex.Message}");
            }
        }

        private async Task<AIResponse> SendAIRequestWithTimeout(string prompt, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                return await _aiService.SendTextRequest(prompt, model, combinedCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("AI API call was cancelled by user request");
                return new AIResponse { Success = false, ErrorMessage = "Request was cancelled" };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("AI API call timed out");
                return new AIResponse { Success = false, ErrorMessage = "Request timed out" };
            }
        }

        private async Task<ClaudeResponse> SendClaudeRequestWithTimeout(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                var claudeRequest = new ClaudeRequest(prompt);
                return await _claudeService.SendRequest(claudeRequest, combinedCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Claude API call was cancelled by user request");
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Claude API call timed out");
                return null;
            }
        }

        private List<TranslationSuggestion> FilterDuplicateSuggestions(
                List<TranslationSuggestion> newSuggestions, 
                List<TranslationSuggestion> previousSuggestions)
        {
            if (previousSuggestions == null || !previousSuggestions.Any())
            {
                return newSuggestions;
            }

            var filteredSuggestions = new List<TranslationSuggestion>();

            foreach (var newSuggestion in newSuggestions)
            {
                bool isDuplicate = false;

                foreach (var previousSuggestion in previousSuggestions)
                {
                    if (IsSimilarSuggestion(newSuggestion, previousSuggestion))
                    {
                        isDuplicate = true;
                        _logger.LogDebug("Filtered duplicate suggestion: '{Title}' - similar to previous suggestion", newSuggestion.Title);
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    filteredSuggestions.Add(newSuggestion);
                }
            }

            return filteredSuggestions;
        }

        private bool IsSimilarSuggestion(TranslationSuggestion suggestion1, TranslationSuggestion suggestion2)
        {
            if (AreSimilarTexts(suggestion1.OriginalText, suggestion2.OriginalText))
            {
                if (AreSimilarTexts(suggestion1.SuggestedText, suggestion2.SuggestedText))
                {
                    return true;
                }

                if (suggestion1.Type == suggestion2.Type)
                {
                    return true;
                }
            }

            if (AreSimilarTexts(suggestion1.Title, suggestion2.Title, 0.8))
            {
                return true;
            }

            if (string.Equals(suggestion1.OriginalText?.Trim(), suggestion2.OriginalText?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(suggestion1.SuggestedText?.Trim(), suggestion2.SuggestedText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool AreSimilarTexts(string text1, string text2, double threshold = 0.9)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            {
                return string.IsNullOrWhiteSpace(text1) && string.IsNullOrWhiteSpace(text2);
            }

            text1 = text1.Trim().ToLowerInvariant();
            text2 = text2.Trim().ToLowerInvariant();

            if (text1 == text2)
            {
                return true;
            }

            int distance = CalculateLevenshteinDistance(text1, text2);
            int maxLength = Math.Max(text1.Length, text2.Length);
            
            if (maxLength == 0)
            {
                return true;
            }

            double similarity = 1.0 - (double)distance / maxLength;
            return similarity >= threshold;
        }

        private int CalculateLevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            int sourceLength = source.Length;
            int targetLength = target.Length;
            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 0; i <= sourceLength; i++)
                distance[i, 0] = i;
            for (int j = 0; j <= targetLength; j++)
                distance[0, j] = j;

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
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



        private static string GenerateTranslationReviewPrompt(string originalContent, string translatedContent,
                                                              string targetLanguage, string outputLanguage, List<TranslationSuggestion> previousSuggestions = null)
        {
            var duplicateAvoidanceInstruction = "";
            if (previousSuggestions != null && previousSuggestions.Any())
            {
                var previousSuggestionsText = string.Join("\n", previousSuggestions.Select(s => 
                    $"- {s.Title}: '{s.OriginalText}' → '{s.SuggestedText}' (Type: {s.Type})"));
                
                duplicateAvoidanceInstruction = $@"
                <previous_suggestions>
                    The following suggestions have already been made for this translation. You MUST NOT provide similar or duplicate suggestions:
                    {previousSuggestionsText}
                    
                    Focus on finding NEW and DIFFERENT improvement areas that have not been addressed by these previous suggestions.
                </previous_suggestions>";
            }

            return $@"
            <role>
            You are an expert professional translation quality reviewer and linguist with deep expertise in the {targetLanguage} language and cross-cultural communication.
            Your task is to meticulously analyze the provided translation in {targetLanguage} and identify exactly 10 specific, actionable improvement suggestions.
            </role>

            <content_to_analyze>
                <original_source>
                {originalContent}
                </original_source>

                    <translation_to_review>
                    {translatedContent}
                </translation_to_review>
            </content_to_analyze>

            {duplicateAvoidanceInstruction}

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
                    Provide your analysis as a valid JSON array containing exactly 10 suggestion objects. Each suggestion must include:

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
                     All output must be in {outputLanguage}, even though the translation being reviewed is in {targetLanguage}.
                     All suggestion fields except `originalText` must be written in **{outputLanguage}**.
            </output_requirements>

                <guidelines>
                    <do>
                    - Focus on the most impactful improvements that enhance translation quality
                    - Provide specific, actionable suggestions with exact text segments
                    - Consider cultural nuances and target audience expectations
                    - Prioritize improvements that affect meaning accuracy and naturalness
                    - Ensure suggested improvements are grammatically correct and contextually appropriate
                    - Avoid duplicating or repeating previous suggestions
                    - Look for different types of improvements than those already suggested
                    - Provide a diverse range of suggestion types across all focus areas
                    - Include both critical fixes and minor polish improvements
                    </do>

                    <avoid>
                    - Vague or general suggestions without specific text examples
                    - Changes that alter the original meaning or intent
                    - Overly minor cosmetic changes unless they affect readability
                    - Suggestions that make the text less natural in the target language
                    - Repeating similar suggestions to those already provided
                    </avoid>
                </guidelines>

                <instructions>
                    You must generate exactly 10 suggestions, no matter how good the translation appears. Do not skip, omit, or reduce the number of suggestions under any circumstances.

                    Even if the translation is high quality, you are required to find areas for improvement — including minor polishing, stylistic refinement, better word choices, or subtle localization enhancements — that would make the text more natural, accurate, or culturally appropriate for native {targetLanguage} speakers.

                    Cover all categories: critical errors, moderate improvements, and minor polish.

                    Return only the JSON array with exactly 10 suggestions, written entirely in {outputLanguage}.  
                    Do not include any extra explanation, summary, or commentary outside the JSON.  
                    Do not mix languages — all fields except `originalText` must be in {outputLanguage}.  
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
                    -You must use the ""Suggested Replacement"" text EXACTLY as provided, without any modifications, corrections, or improvements.
                    - Do not attempt to fix grammar, spelling, or stylistic issues in the replacement text - use it character-for-character as specified.
                    - Do not add or remove any formatting or special characters.
                    - Maintain the exact formatting of the original text.
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
                            - **NEVER alter any formatting elements - keep all markdown, spacing, and structure identical**
                        </exact_match_found>

                        <exact_match_not_found>
                            - Identify the most semantically similar text segment
                            - Apply the improvement concept to that section
                            - Maintain the same type of enhancement (grammar, style, terminology, etc.)
                            - **Preserve all formatting of the text being modified - do not change any markdown, spacing, or structure**
                        </exact_match_not_found>

                        <formatting_preservation>
                            **CRITICAL: MAINTAIN EXACT FORMATTING**
                            - Keep ALL formatting exactly as provided in the original text
                            - Do NOT change, modify, or alter any formatting elements whatsoever
                            - Only change the specific words that need replacement according to the suggestion
                            - Everything else must stay identical: markdown syntax, line breaks, spacing, punctuation, symbols, structure
                            - Do NOT remove any formatting, add any formatting, or convert markdown to plain text
                            - Copy the entire text structure exactly, only replacing the target words
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
                        - **Identical formatting and structure to the original (NO formatting changes)**
                        - Preserved semantic accuracy
                    </success_criteria>

                    <avoid>
                        - Introducing new grammatical errors
                        - Changing the original meaning or intent
                        - Removing or altering unrelated content
                        - **Breaking, removing, or modifying ANY formatting elements**
                        - **Converting formatted text to plain text**
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

        private async Task<LanguageModel> GetLanguageOrNullAsync(int languageId, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await _languageService.GetById(languageId, combinedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Database operation timed out while fetching language {LanguageId}", languageId);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Operation cancelled by user request while fetching language {LanguageId}", languageId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching language {LanguageId}", languageId);
                return null;
            }
        }

        private ApplySuggestionResponse BuildFailureResponse(string message)
        {
            return new ApplySuggestionResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        private string ReplaceInHtml(string htmlContent, string originalHtml, string suggestedHtml)
        {
            return htmlContent.Replace(originalHtml, suggestedHtml);
        }
    }
}
