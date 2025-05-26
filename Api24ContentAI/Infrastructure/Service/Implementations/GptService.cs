using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class GptService : IGptService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GptService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public GptService(HttpClient httpClient, IConfiguration configuration, ILogger<GptService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public async Task<VerificationResult> VerifyResponseQuality(ClaudeRequest request, ClaudeResponse response, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting response quality verification with GPT");
                
                string responseContent = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
                
                if (string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogWarning("Empty response content detected during verification");
                    return new VerificationResult { 
                        Success = false, 
                        ErrorMessage = "Empty response content" 
                    };
                }

                string verificationPrompt = $"""
                                                             You are a quality assurance expert. Evaluate the following AI response for quality, accuracy, and relevance.
                                                             
                                                             Original request:
                                                             {request.Messages}
                                                             
                                                             AI response:
                                                             {responseContent}
                                                             
                                                             Rate the response on a scale from 0.0 to 1.0 where:
                                                             - 0.0 means completely irrelevant, inaccurate, or low quality
                                                             - 1.0 means perfect quality, highly accurate and relevant
                                                             
                                                             Provide your rating as a single decimal number between 0.0 and 1.0, followed by a brief explanation.
                                                             Format: <rating>|<explanation>
                                                             
                                             """;

                var gptRequest = new
                {
                    model = GetDefaultModel(),
                    messages = new[]
                    {
                        new { role = "system", content = "You are a quality assurance expert evaluating AI responses." },
                        new { role = "user", content = verificationPrompt }
                    },
                    temperature = 0.3
                };

                _logger.LogInformation("Sending verification request to GPT API");
                
                _logger.LogInformation("GPT Request: {Request}", JsonSerializer.Serialize(gptRequest, _jsonOptions));
                
                var gptResponse = await SendToGptApi(gptRequest, cancellationToken);
                
                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning("Empty response from verification service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from verification service" };
                }

                _logger.LogInformation("GPT verification response: {Response}", gptResponse);

                string[] parts = gptResponse.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out var rating))
                {
                    _logger.LogWarning("Failed to parse verification response: {Response}", gptResponse);
                    return new VerificationResult { Success = false, ErrorMessage = "Failed to parse verification response" };
                }

                _logger.LogInformation("Verification completed. Quality score: {Score}", rating);
                
                return new VerificationResult
                {
                    Success = true,
                    QualityScore = Math.Clamp(rating, 0.0, 1.0),
                    Feedback = parts[1].Trim()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during response quality verification");
                return new VerificationResult
                {
                    Success = false,
                    ErrorMessage = $"Verification failed: {ex.Message}"
                };
            }
        }

        public async Task<VerificationResult> VerifyTranslationBatch(List<KeyValuePair<int, string>> translations, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting translation verification with GPT service for {Count} translations", translations?.Count ?? 0);
                
                if (translations == null || translations.Count == 0)
                {
                    _logger.LogWarning("Empty translation batch provided for verification");
                    return new VerificationResult { 
                        Success = false, 
                        ErrorMessage = "No translations provided for verification" 
                    };
                }

                var samples = translations;
                
                _logger.LogInformation("Selected {SampleCount} samples for verification", samples.Count);

                double totalScore = 0;
                var feedbacks = new List<string>();
                var chunkWarnings = new Dictionary<int, string>();
                var verifiedChunks = 0;

                foreach (var sample in samples)
                {
                    _logger.LogInformation("Verifying translation chunk {ChunkId}", sample.Key);
                    
                    if (string.IsNullOrWhiteSpace(sample.Value))
                    {
                        _logger.LogWarning("Empty translation chunk {ChunkId}", sample.Key);
                        chunkWarnings.Add(sample.Key, "Empty translation chunk");
                        continue;
                    }

                    string sampleText = sample.Value;
                    if (sampleText.Length > 3000)
                    {
                        _logger.LogWarning("Translation chunk {ChunkId} is too large ({Length} chars), truncating to 3000 chars", 
                            sample.Key, sampleText.Length);
                        sampleText = sampleText.Substring(0, 3000) + "... [truncated for verification]";
                    }

                    string verificationPrompt = $"""
                                                 You are a translation quality expert. Evaluate the following translated text for quality, accuracy, and fluency.
                                                 
                                                 This is a translation of a document chunk (chunk ID: {sample.Key}).
                                                 The text has been translated to another language.
                                                 
                                                 Translated text:
                                                 {sampleText}
                                                 
                                                 Even without seeing the original text, evaluate the translation quality based on:
                                                 1. Fluency and naturalness of language
                                                 2. Consistency of terminology and style
                                                 3. Absence of obvious translation errors
                                                 4. Proper formatting and structure
                                                 5. Check for untranslated text or codes that should have remained untranslated
                                                 6. Proper handling of mathematical formulas:
                                                    - All mathematical formulas should be in LaTeX format (enclosed in $ or $$ delimiters)
                                                    - Complex equations should use proper LaTeX notation
                                                    - Variables and mathematical symbols should be preserved correctly
                                                 
                                                 Be VERY CRITICAL in your evaluation. If you see any of the following issues, reduce the score significantly:
                                                 - Untranslated words that should have been translated
                                                 - Mixed languages in the same text
                                                 - Inconsistent terminology
                                                 - Awkward phrasing or unnatural language
                                                 - Formatting issues
                                                 - Mathematical formulas not properly formatted in LaTeX
                                                 
                                                 Rate the translation on a scale from 0.0 to 1.0 where:
                                                 - 0.0 means poor quality, potentially machine-translated text with errors
                                                 - 0.5 means average quality with some issues
                                                 - 1.0 means professional quality, fluent and accurate translation with no issues
                                                 
                                                 Provide your rating as a single decimal number between 0.0 and 1.0, followed by a brief explanation.
                                                 Format: <rating>|<explanation>
                                                 """;

                    var gptRequest = new
                    {
                        model = GetDefaultModel(),
                        messages = new[]
                        {
                            new { role = "system", content = "You are a translation quality expert evaluating translated text. Always provide a rating even with limited context. Be very critical and thorough in your evaluation. Pay special attention to mathematical formulas - they should be properly formatted in LaTeX with $ or $$ delimiters." },
                            new { role = "user", content = verificationPrompt }
                        },
                        temperature = 0.3
                    };

                    _logger.LogInformation("Sending verification request to GPT API for chunk {ChunkId}", sample.Key);
                    
                    try
                    {
                        var gptResponse = await SendToGptApiWithRetry(gptRequest, cancellationToken);
                        
                        if (string.IsNullOrEmpty(gptResponse))
                        {
                            _logger.LogWarning("Failed to get verification for chunk {ChunkId}", sample.Key);
                            chunkWarnings.Add(sample.Key, "Failed to get verification for this chunk");
                            continue;
                        }

                        if (!gptResponse.Contains("|"))
                        {
                            _logger.LogWarning("Response doesn't contain expected format for chunk {ChunkId}: {Response}", sample.Key, gptResponse);
                            
                            double defaultRating = 0.7; // Reasonable default
                            feedbacks.Add($"Chunk {sample.Key}: Unable to parse rating. GPT response: {gptResponse}");
                            totalScore += defaultRating;
                            verifiedChunks++;
                            continue;
                        }

                        string[] parts = gptResponse.Split('|', 2);
                        if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                        {
                            _logger.LogWarning("Failed to parse verification response for chunk {ChunkId}: {Response}", sample.Key, gptResponse);
                            chunkWarnings.Add(sample.Key, "Failed to parse verification response for this chunk");
                            continue;
                        }

                        _logger.LogInformation("Chunk {ChunkId} verification score: {Score}", sample.Key, rating);
                        totalScore += Math.Clamp(rating, 0.0, 1.0);
                        feedbacks.Add($"Chunk {sample.Key}: {parts[1].Trim()}");
                        verifiedChunks++;
                    }
                    catch (Exception ex) when (ex.Message.Contains("context_length_exceeded"))
                    {
                        _logger.LogWarning("Context length exceeded for chunk {ChunkId}, trying with shorter sample", sample.Key);
                        
                        if (sampleText.Length > 1000)
                        {
                            sampleText = sampleText.Substring(0, 1000) + "... [truncated for verification]";
                            
                            verificationPrompt = $"""
                                                  You are a translation quality expert. Evaluate the following translated text sample for quality.
                                                  
                                                  Translated text sample (chunk ID: {sample.Key}):
                                                  {sampleText}
                                                  
                                                  Rate the translation quality from 0.0 to 1.0 based on fluency, consistency, and absence of errors.
                                                  Format: <rating>|<brief explanation>
                                                  """;
                            
                            var shorterRequest = new
                            {
                                model = GetDefaultModel(),
                                messages = new[]
                                {
                                    new { role = "system", content = "You are a translation quality expert. Be concise." },
                                    new { role = "user", content = verificationPrompt }
                                },
                                temperature = 0.3
                            };
                            
                            try
                            {
                                var gptResponse = await SendToGptApiWithRetry(shorterRequest, cancellationToken);
                                
                                if (!string.IsNullOrEmpty(gptResponse) && gptResponse.Contains("|"))
                                {
                                    string[] parts = gptResponse.Split('|', 2);
                                    if (parts.Length >= 2 && double.TryParse(parts[0].Trim(), out double rating))
                                    {
                                        _logger.LogInformation("Chunk {ChunkId} verification score (with shorter sample): {Score}", sample.Key, rating);
                                        totalScore += Math.Clamp(rating, 0.0, 1.0);
                                        feedbacks.Add($"Chunk {sample.Key}: {parts[1].Trim()} (based on truncated sample)");
                                        verifiedChunks++;
                                    }
                                    else
                                    {
                                        chunkWarnings.Add(sample.Key, "Failed to parse verification response even with shorter sample");
                                    }
                                }
                                else
                                {
                                    chunkWarnings.Add(sample.Key, "Failed to get verification even with shorter sample");
                                }
                            }
                            catch (Exception innerEx)
                            {
                                _logger.LogError(innerEx, "Error verifying chunk {ChunkId} with shorter sample", sample.Key);
                                chunkWarnings.Add(sample.Key, $"Error verifying: {innerEx.Message}");
                            }
                        }
                        else
                        {
                            chunkWarnings.Add(sample.Key, "Sample too large for verification");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error verifying chunk {ChunkId}", sample.Key);
                        chunkWarnings.Add(sample.Key, $"Error verifying: {ex.Message}");
                    }
                }

                if (verifiedChunks == 0)
                {
                    _logger.LogWarning("Failed to verify any translation samples");
                    return new VerificationResult { 
                        Success = false, 
                        ErrorMessage = "Failed to verify any translation samples",
                        ChunkWarnings = chunkWarnings
                    };
                }

                double averageScore = totalScore / verifiedChunks;
                _logger.LogInformation("Translation verification completed. Average score: {Score}, Verified chunks: {VerifiedChunks}/{TotalChunks}", 
                    averageScore, verifiedChunks, translations.Count);
                
                return new VerificationResult
                {
                    Success = true,
                    QualityScore = averageScore,
                    Feedback = string.Join("\n", feedbacks),
                    VerifiedChunks = verifiedChunks,
                    RecoveredChunks = translations.Count,
                    ChunkWarnings = chunkWarnings
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during translation batch verification");
                return new VerificationResult
                {
                    Success = false,
                    ErrorMessage = $"Translation verification failed: {ex.Message}"
                };
            }
        }

        public async Task<VerificationResult> EvaluateTranslationQuality(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting translation quality evaluation with GPT");
                
                if (prompt.Length > 6000)
                {
                    _logger.LogWarning("Prompt is too long ({Length} chars), truncating to 6000 chars", prompt.Length);
                    prompt = prompt.Substring(0, 6000) + "... [truncated for evaluation]";
                }
                
                var gptRequest = new
                {
                    model = GetDefaultModel(),
                    messages = new[]
                    {
                        new { role = "system", content = "You are a translation quality expert evaluating translated text. Focus on fluency and naturalness of the language, not on comparing to an original text. Be very critical in your evaluation. If you see any untranslated text that should have been translated, mixed languages, inconsistent terminology, or improperly handled mathematical formulas, reduce the score significantly. Mathematical formulas should be properly formatted in LaTeX (enclosed in $ or $$ delimiters). Variables and mathematical symbols within formulas should be preserved exactly as they appear in the original." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3
                };

                _logger.LogInformation("Sending evaluation request to GPT API");
                
                _logger.LogInformation("GPT Request: {Request}", JsonSerializer.Serialize(gptRequest, _jsonOptions));
                
                var gptResponse = await SendToGptApiWithRetry(gptRequest, cancellationToken);
                
                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning("Empty response from GPT evaluation service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from evaluation service" };
                }

                _logger.LogInformation("GPT evaluation response: {Response}", gptResponse);

                string[] parts = gptResponse.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                {
                    _logger.LogWarning("Failed to parse evaluation response: {Response}", gptResponse);
                    
                    var ratingMatch = Regex.Match(gptResponse, @"(\d+\.\d+)");
                    if (ratingMatch.Success && double.TryParse(ratingMatch.Value, out rating))
                    {
                        _logger.LogInformation("Extracted rating from unformatted response: {Rating}", rating);
                        return new VerificationResult
                        {
                            Success = true,
                            QualityScore = Math.Clamp(rating, 0.0, 1.0),
                            Feedback = gptResponse
                        };
                    }
                    
                    _logger.LogWarning("Using default rating of 0.7 due to parsing failure");
                    return new VerificationResult
                    {
                        Success = true,
                        QualityScore = 0.7,
                        Feedback = "Could not parse rating from response: " + gptResponse
                    };
                }

                _logger.LogInformation("Evaluation completed. Quality score: {Score}", rating);
                
                return new VerificationResult
                {
                    Success = true,
                    QualityScore = Math.Clamp(rating, 0.0, 1.0),
                    Feedback = parts[1].Trim()
                };
            }
            catch (Exception ex) when (ex.Message.Contains("context_length_exceeded"))
            {
                _logger.LogWarning("Context length exceeded during evaluation, trying with shorter prompt");
                
                string shorterPrompt = "Evaluate this translation sample for quality. Rate from 0.0 to 1.0.\n\n";
                
                var match = Regex.Match(prompt, @"Sample \d+:\s*([\s\S]{1,2000})(?:\n\n---|$)");
                if (match.Success)
                {
                    shorterPrompt += match.Groups[1].Value;
                }
                else
                {
                    shorterPrompt += prompt.Substring(0, Math.Min(prompt.Length, 2000));
                }
                
                shorterPrompt += "\n\nCheck especially for proper formatting of mathematical formulas in LaTeX (with $ or $$ delimiters).\nProvide rating as: <rating>|<brief explanation>";
                
                var shorterRequest = new
                {
                    model = GetDefaultModel(),
                    messages = new[]
                    {
                        new { role = "system", content = "You are a translation quality expert. Be concise. Pay special attention to mathematical formulas - they should be properly formatted in LaTeX." },
                        new { role = "user", content = shorterPrompt }
                    },
                    temperature = 0.3
                };
                
                try
                {
                    var gptResponse = await SendToGptApiWithRetry(shorterRequest, cancellationToken);
                    
                    if (string.IsNullOrEmpty(gptResponse))
                    {
                        return new VerificationResult { Success = false, ErrorMessage = "Empty response from evaluation service" };
                    }
                    
                    string[] parts = gptResponse.Split('|', 2);
                    if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                    {
                        var ratingMatch = Regex.Match(gptResponse, @"(\d+\.\d+)");
                        if (ratingMatch.Success && double.TryParse(ratingMatch.Value, out rating))
                        {
                            return new VerificationResult
                            {
                                Success = true,
                                QualityScore = Math.Clamp(rating, 0.0, 1.0),
                                Feedback = gptResponse + " (based on truncated sample)"
                            };
                        }
                        
                        return new VerificationResult
                        {
                            Success = true,
                            QualityScore = 0.7,
                            Feedback = "Could not parse rating from response: " + gptResponse + " (based on truncated sample)"
                        };
                    }
                    
                    return new VerificationResult
                    {
                        Success = true,
                        QualityScore = Math.Clamp(rating, 0.0, 1.0),
                        Feedback = parts[1].Trim() + " (based on truncated sample)"
                    };
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Error during shortened translation quality evaluation");
                    return new VerificationResult
                    {
                        Success = false,
                        ErrorMessage = $"Evaluation failed: {innerEx.Message}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during translation quality evaluation");
                return new VerificationResult
                {
                    Success = false,
                    ErrorMessage = $"Evaluation failed: {ex.Message}"
                };
            }
        }

        private async Task<string> SendToGptApi(object request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Preparing to send request to GPT API");
            
            string apiKey = _configuration.GetSection("Security:OpenAIApiKey").Value;
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("OpenAI API key is missing in configuration");
                throw new InvalidOperationException("OpenAI API key is missing in configuration.");
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, _jsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json"
                )
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
            
            _logger.LogInformation("Sending request to GPT API");
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            _logger.LogInformation("GPT Response: {Response}", responseContent);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GPT API error: {StatusCode}, {ErrorContent}", response.StatusCode, responseContent);
                throw new Exception($"GPT API error: {response.StatusCode}, {responseContent}");
            }

            _logger.LogInformation("Received successful response from GPT API");
            
            using var jsonDoc = JsonDocument.Parse(responseContent);
            
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        
        private async Task<string> SendToGptApiWithRetry(object request, CancellationToken cancellationToken, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation("GPT API call attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    return await SendToGptApi(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "GPT API call failed (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    
                    if (attempt < maxRetries)
                    {
                        int delayMs = (int)Math.Pow(2, attempt) * 500;
                        _logger.LogInformation("Retrying GPT API call in {DelayMs}ms", delayMs);
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }
            }
            
            _logger.LogError(lastException, "All GPT API call attempts failed");
            return string.Empty;
        }
        
        private string GetDefaultModel()
        {
            string configModel = _configuration.GetSection("OpenAI:DefaultModel").Value;
            return !string.IsNullOrEmpty(configModel) ? configModel : "gpt-4o";
        }
    }
}
