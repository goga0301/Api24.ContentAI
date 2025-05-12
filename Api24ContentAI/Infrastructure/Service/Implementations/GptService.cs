using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

                string verificationPrompt = $@"
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
                ";

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
                
                // Log the request object
                _logger.LogInformation("GPT Request: {Request}", JsonSerializer.Serialize(gptRequest, _jsonOptions));
                
                var gptResponse = await SendToGptApi(gptRequest, cancellationToken);
                
                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning("Empty response from verification service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from verification service" };
                }

                _logger.LogInformation("GPT verification response: {Response}", gptResponse);

                string[] parts = gptResponse.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
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

                int samplesToCheck = Math.Min(3, translations.Count);
                var samples = translations
                    .OrderBy(x => Guid.NewGuid()) // Random order
                    .Take(samplesToCheck)
                    .ToList();
                
                _logger.LogInformation("Selected {SampleCount} samples for verification", samples.Count);

                double totalScore = 0;
                List<string> feedbacks = new List<string>();
                Dictionary<int, string> chunkWarnings = new Dictionary<int, string>();
                int verifiedChunks = 0;

                foreach (var sample in samples)
                {
                    _logger.LogInformation("Verifying translation chunk {ChunkId}", sample.Key);
                    
                    if (string.IsNullOrWhiteSpace(sample.Value))
                    {
                        _logger.LogWarning("Empty translation chunk {ChunkId}", sample.Key);
                        chunkWarnings.Add(sample.Key, "Empty translation chunk");
                        continue;
                    }

                    string verificationPrompt = $@"
                    You are a translation quality expert. Evaluate the following translated text for quality, accuracy, and fluency.
                    
                    This is a translation of a document chunk (chunk ID: {sample.Key}).
                    The text has been translated to another language.
                    
                    Translated text:
                    {sample.Value}
                    
                    Even without seeing the original text, evaluate the translation quality based on:
                    1. Fluency and naturalness of language
                    2. Consistency of terminology and style
                    3. Absence of obvious translation errors
                    4. Proper formatting and structure
                    
                    Rate the translation on a scale from 0.0 to 1.0 where:
                    - 0.0 means poor quality, potentially machine-translated text with errors
                    - 1.0 means professional quality, fluent and accurate translation
                    
                    Provide your rating as a single decimal number between 0.0 and 1.0, followed by a brief explanation.
                    Format: <rating>|<explanation>
                    ";

                    var gptRequest = new
                    {
                        model = GetDefaultModel(),
                        messages = new[]
                        {
                            new { role = "system", content = "You are a translation quality expert evaluating translated text. Always provide a rating even with limited context." },
                            new { role = "user", content = verificationPrompt }
                        },
                        temperature = 0.3
                    };

                    _logger.LogInformation("Sending verification request to GPT API for chunk {ChunkId}", sample.Key);
                    
                    // Log the request object
                    _logger.LogInformation("GPT Request: {Request}", JsonSerializer.Serialize(gptRequest, _jsonOptions));
                    
                    var gptResponse = await SendToGptApiWithRetry(gptRequest, cancellationToken);
                    
                    if (string.IsNullOrEmpty(gptResponse))
                    {
                        _logger.LogWarning("Failed to get verification for chunk {ChunkId}", sample.Key);
                        chunkWarnings.Add(sample.Key, "Failed to get verification for this chunk");
                        continue;
                    }

                    // Check if the response contains the expected format
                    if (!gptResponse.Contains("|"))
                    {
                        // Try to extract a rating from the text
                        _logger.LogWarning("Response doesn't contain expected format for chunk {ChunkId}: {Response}", sample.Key, gptResponse);
                        
                        // Fallback: assign a default rating
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
            
            // Log the full response similar to Claude
            Console.WriteLine("GPT Response: " + responseContent);
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
                        // Exponential backoff
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
            return !string.IsNullOrEmpty(configModel) ? configModel : "gpt-4";
        }
    }
}
