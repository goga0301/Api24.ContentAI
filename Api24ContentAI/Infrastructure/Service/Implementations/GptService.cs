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
                // Extract the content from the response
                string responseContent = response.Content?.FirstOrDefault()?.Text ?? string.Empty;
                
                if (string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogWarning("Empty response content detected during verification");
                    return new VerificationResult { 
                        Success = false, 
                        ErrorMessage = "Empty response content" 
                    };
                }

                // Create a verification prompt
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

                // Create a request to send to GPT
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

                // Send the request to the OpenAI API
                var gptResponse = await SendToGptApi(gptRequest, cancellationToken);
                
                // Parse the response
                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning("Empty response from verification service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from verification service" };
                }

                // Extract rating and explanation
                string[] parts = gptResponse.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                {
                    _logger.LogWarning("Failed to parse verification response: {Response}", gptResponse);
                    return new VerificationResult { Success = false, ErrorMessage = "Failed to parse verification response" };
                }

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
                if (translations == null || translations.Count == 0)
                {
                    _logger.LogWarning("Empty translation batch provided for verification");
                    return new VerificationResult { 
                        Success = false, 
                        ErrorMessage = "No translations provided for verification" 
                    };
                }

                // For batch translations, we'll sample a few translations to verify
                int samplesToCheck = Math.Min(3, translations.Count);
                var samples = translations
                    .OrderBy(x => Guid.NewGuid()) // Random order
                    .Take(samplesToCheck)
                    .ToList();

                double totalScore = 0;
                List<string> feedbacks = new List<string>();
                Dictionary<int, string> chunkWarnings = new Dictionary<int, string>();
                int verifiedChunks = 0;

                foreach (var sample in samples)
                {
                    if (string.IsNullOrWhiteSpace(sample.Value))
                    {
                        chunkWarnings.Add(sample.Key, "Empty translation chunk");
                        continue;
                    }

                    string verificationPrompt = $@"
                    You are a translation quality expert. Evaluate the following translation for quality, accuracy, and fluency.
                    
                    Translation (chunk {sample.Key}):
                    {sample.Value}
                    
                    Rate the translation on a scale from 0.0 to 1.0 where:
                    - 0.0 means poor quality, potentially machine-translated text with errors
                    - 1.0 means professional quality, fluent and accurate translation
                    
                    Provide your rating as a single decimal number between 0.0 and 1.0, followed by a brief explanation.
                    Format: <rating>|<explanation>
                    ";

                    // Create a request to send to GPT
                    var gptRequest = new
                    {
                        model = GetDefaultModel(),
                        messages = new[]
                        {
                            new { role = "system", content = "You are a translation quality expert." },
                            new { role = "user", content = verificationPrompt }
                        },
                        temperature = 0.3
                    };

                    // Send the request to the OpenAI API with retry
                    var gptResponse = await SendToGptApiWithRetry(gptRequest, cancellationToken);
                    
                    // Parse the response
                    if (string.IsNullOrEmpty(gptResponse))
                    {
                        chunkWarnings.Add(sample.Key, "Failed to get verification for this chunk");
                        continue;
                    }

                    // Extract rating and explanation
                    string[] parts = gptResponse.Split('|', 2);
                    if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                    {
                        chunkWarnings.Add(sample.Key, "Failed to parse verification response for this chunk");
                        continue;
                    }

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
            string apiKey = _configuration.GetSection("Security:OpenAIApiKey").Value;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is missing in configuration.");
            }

            // Set up the HTTP request
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, _jsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json"
                )
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");

            // Send the request
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("GPT API error: {StatusCode}, {ErrorContent}", response.StatusCode, errorContent);
                throw new Exception($"GPT API error: {response.StatusCode}, {errorContent}");
            }

            // Parse the response
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(responseContent);
            
            // Extract the message content
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
                    return await SendToGptApi(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "API call failed (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    
                    if (attempt < maxRetries)
                    {
                        // Exponential backoff
                        int delayMs = (int)Math.Pow(2, attempt) * 500;
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }
            }
            
            _logger.LogError(lastException, "All API call attempts failed");
            return string.Empty;
        }
        
        private string GetDefaultModel()
        {
            string configModel = _configuration.GetSection("OpenAI:DefaultModel").Value;
            return !string.IsNullOrEmpty(configModel) ? configModel : "gpt-4";
        }
    }
}