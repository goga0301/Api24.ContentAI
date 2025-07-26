using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiService> _logger;
        private bool _headersInitialized = false;
        private string _apiKey;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        }

        public async Task<GeminiResponse> SendRequest(GeminiRequest request, CancellationToken cancellationToken)
        {
            try
            {
                EnsureHeaders();
                
                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                string jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                _logger.LogDebug("Gemini API request size: {Size} bytes", jsonContent.Length);

                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                var url = $"v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, httpContent, cancellationToken);

                var responseStr = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Gemini API response: {Response}", responseStr);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini API error: {StatusCode} - {Response}", response.StatusCode, responseStr);
                    throw new Exception($"Gemini API error: {responseStr}");
                }

                return JsonSerializer.Deserialize<GeminiResponse>(responseStr, jsonOptions) 
                       ?? throw new Exception("Failed to deserialize Gemini response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Gemini integration service");
                throw new Exception("Error in Gemini integration service: " + ex.Message, ex);
            }
        }

        public async Task<GeminiResponse> SendRequestWithFile(List<GeminiPart> parts, CancellationToken cancellationToken)
        {
            try
            {
                EnsureHeaders();
                
                _logger.LogDebug("Sending request to Gemini API with {Count} parts", parts.Count);
                
                var processedParts = await ProcessPartsWithScreenshotService(parts, cancellationToken);
                
                bool hasImages = processedParts.Any(p => p.InlineData != null);
                int totalImageDataSize = processedParts.Where(p => p.InlineData != null)
                                             .Sum(p => p.InlineData.Data?.Length ?? 0);
                
                var request = new GeminiRequest(processedParts);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                string jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                _logger.LogDebug("Gemini API request size: {Size} bytes, has images: {HasImages}, total image data: {ImageDataSize}", 
                    jsonContent.Length, hasImages, totalImageDataSize);

                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                TimeSpan timeout = DetermineGeminiTimeout(jsonContent.Length, hasImages, totalImageDataSize);
                
                using (var timeoutCts = new CancellationTokenSource(timeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    if (hasImages || jsonContent.Length > 500000) // 500KB
                    {
                        _logger.LogInformation("Gemini request contains images or is large (Size: {Size} bytes, Images: {HasImages}, Image data: {ImageDataSize}), using extended timeout of {Timeout} seconds", 
                            jsonContent.Length, hasImages, totalImageDataSize, timeout.TotalSeconds);
                    }

                    var url = $"v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
                    var response = await _httpClient.PostAsync(url, httpContent, linkedCts.Token);

                    var responseStr = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("Gemini API response: {Response}", responseStr);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Gemini API error: {StatusCode} - {Response}", response.StatusCode, responseStr);
                        throw new Exception($"Gemini API error: {responseStr}");
                    }

                    var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseStr, jsonOptions);
                    if (geminiResponse == null)
                    {
                        _logger.LogError("Failed to deserialize Gemini response. Raw response: {Response}", responseStr);
                        throw new Exception("Failed to deserialize Gemini response");
                    }

                    var content = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("Gemini returned empty or null content. Response structure: Candidates={CandidateCount}", 
                            geminiResponse.Candidates?.Count ?? 0);
                        
                        if (geminiResponse.Candidates?.Any() == true)
                        {
                            var candidate = geminiResponse.Candidates.First();
                            _logger.LogWarning("First candidate details: FinishReason={FinishReason}, ContentParts={ContentParts}", 
                                candidate.FinishReason ?? "null", 
                                candidate.Content?.Parts?.Count ?? 0);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Gemini returned content with {Length} characters", content.Length);
                    }

                    return geminiResponse;
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Gemini API request was cancelled or timed out");
                throw new Exception("Gemini API request timed out. The image processing is taking longer than expected. Please try again or use a smaller image.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Gemini integration service");
                throw new Exception("Error in Gemini integration service: " + ex.Message, ex);
            }
        }

        private async Task<List<GeminiPart>> ProcessPartsWithScreenshotService(List<GeminiPart> parts, CancellationToken cancellationToken)
        {
            var processedParts = new List<GeminiPart>();
            
            foreach (var part in parts)
            {
                if (part.InlineData != null && IsUnsupportedFileType(part.InlineData.MimeType))
                {
                    _logger.LogInformation("Converting unsupported file type {MimeType} using screenshot service", part.InlineData.MimeType);
                    
                    try
                    {
                        var screenshotParts = await ConvertFileToScreenshots(part.InlineData, cancellationToken);
                        processedParts.AddRange(screenshotParts);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to convert file using screenshot service for MIME type: {MimeType}", part.InlineData.MimeType);
                        processedParts.Add(part);
                    }
                }
                else
                {
                    processedParts.Add(part);
                }
            }
            
            return processedParts;
        }

        private static bool IsUnsupportedFileType(string mimeType)
        {
            var unsupportedTypes = new[]
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
                "application/msword", // .doc
                "application/vnd.ms-word" // .doc
            };
            
            return unsupportedTypes.Contains(mimeType);
        }

        private async Task<List<GeminiPart>> ConvertFileToScreenshots(GeminiInlineData fileData, CancellationToken cancellationToken)
        {
            var results = new List<GeminiPart>();
            
            try
            {
                var fileBytes = Convert.FromBase64String(fileData.Data);
                var tempFileName = GetTempFileName(fileData.MimeType);
                
                await System.IO.File.WriteAllBytesAsync(tempFileName, fileBytes, cancellationToken);
                
                try
                {
                    var screenshotResult = await CallScreenshotService(tempFileName, fileData.MimeType, cancellationToken);
                    
                    foreach (var page in screenshotResult.Pages)
                    {
                        if (!string.IsNullOrWhiteSpace(page.Text))
                        {
                            results.Add(new GeminiPart { Text = $"Extracted text from page {page.Page}: {page.Text}" });
                        }
                        
                        foreach (var screenshot in page.ScreenShots)
                        {
                            if (!string.IsNullOrWhiteSpace(screenshot))
                            {
                                results.Add(new GeminiPart
                                {
                                    InlineData = new GeminiInlineData
                                    {
                                        MimeType = "image/png",
                                        Data = screenshot
                                    }
                                });
                            }
                        }
                    }
                }
                finally
                {
                    if (System.IO.File.Exists(tempFileName))
                    {
                        System.IO.File.Delete(tempFileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting file to screenshots");
                throw;
            }
            
            return results;
        }

        private async Task<ScreenShotResult> CallScreenshotService(string filePath, string mimeType, CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
            using var content = new MultipartFormDataContent();
            using var fileStream = System.IO.File.OpenRead(filePath);
            using var streamContent = new StreamContent(fileStream);
            
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            content.Add(streamContent, "file", System.IO.Path.GetFileName(filePath));
            
            var endpoint = GetScreenshotEndpoint(mimeType);
            
            _logger.LogInformation("Sending file to screenshot service endpoint: {Endpoint}", endpoint);
            
            var response = await httpClient.PostAsync($"http://127.0.0.1:8000/{endpoint}", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<ScreenShotResult>(cancellationToken: cancellationToken);
            if (result == null)
                throw new InvalidOperationException("Screenshot service returned null");
                
            return result;
        }

        private static string GetScreenshotEndpoint(string mimeType)
        {
            return mimeType switch
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "process-word",
                "application/msword" => "process-word",
                "application/vnd.ms-word" => "process-word",
                "application/pdf" => "screenshot",
                _ => "screenshot" // Default to general screenshot endpoint
            };
        }

        private static string GetTempFileName(string mimeType)
        {
            var extension = mimeType switch
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/msword" => ".doc",
                "application/vnd.ms-word" => ".doc",
                "application/pdf" => ".pdf",
                _ => ".tmp"
            };
            
            return System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + extension;
        }

        private static TimeSpan DetermineGeminiTimeout(int requestSize, bool hasImages, int totalImageDataSize)
        {
            TimeSpan baseTimeout = requestSize switch
            {
                > 5000000 => TimeSpan.FromMinutes(7),   // 5MB+ = 7 minutes
                > 2000000 => TimeSpan.FromMinutes(5),   // 2MB+ = 5 minutes  
                > 1000000 => TimeSpan.FromMinutes(3),   // 1MB+ = 3 minutes
                > 500000 => TimeSpan.FromMinutes(2.5),  // 500KB+ = 2.5 minutes
                _ => TimeSpan.FromMinutes(2)            // Default = 2 minutes
            };

            if (hasImages)
            {
                var imageProcessingTime = totalImageDataSize switch
                {
                    > 1000000 => TimeSpan.FromMinutes(2.5), // 1MB+ image data = +2.5 min
                    > 500000 => TimeSpan.FromMinutes(2),    // 500KB+ image data = +2 min  
                    > 200000 => TimeSpan.FromMinutes(1.5),  // 200KB+ image data = +1.5 min
                    > 100000 => TimeSpan.FromMinutes(1),    // 100KB+ image data = +1 min
                    _ => TimeSpan.FromSeconds(45)           // Any image = +45 sec
                };
                
                baseTimeout = baseTimeout.Add(imageProcessingTime);
            }

            var minimumTimeout = TimeSpan.FromMinutes(2);
            return baseTimeout < minimumTimeout ? minimumTimeout : baseTimeout;
        }

        private void EnsureHeaders()
        {
            if (_headersInitialized)
            {
                return;
            }

            _apiKey = _configuration.GetSection("Security:GeminiApiKey").Value;
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Gemini API key is missing in configuration.");
            }

            _httpClient.Timeout = TimeSpan.FromMinutes(1);
            
            _headersInitialized = true;
        }

        public async Task<VerificationResult> VerifyResponseQuality(ClaudeRequest request, ClaudeResponse response, CancellationToken cancellationToken)
        {
            try
            {

                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                _logger.LogInformation("Starting response quality verification with gemini");

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

                var geminiRequest = new GeminiRequest(new List<GeminiPart> {
                        new GeminiPart{ Text = verificationPrompt}
                        });
                _logger.LogInformation("Sending verification request to gemini API");

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("gemini Request for VerifyResponseQuality: {Request}", JsonSerializer.Serialize(geminiRequest, jsonOptions));
                }
                else
                {
                    _logger.LogInformation("Sending verification request to gemini API. Model: Gemini Flash 2.5");
                }

                var geminiResponse = await SendRequest(geminiRequest, cancellationToken);

                var geminiContent = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                if (string.IsNullOrEmpty(geminiContent))
                {
                    _logger.LogWarning("Empty response from verification service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from verification service" };
                }
                _logger.LogInformation("gemini verification response: {Response}",geminiContent );

                string[] parts = geminiContent.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out var rating))
                {
                    _logger.LogWarning("Failed to parse verification response: {Response}", geminiContent);
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
                _logger.LogInformation("Starting translation verification with gemini service for {Count} translations", translations?.Count ?? 0);

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
                        - All mathematical formulas should use regular text characters (no LaTeX formatting)
                        - Use Unicode symbols where appropriate (e.g., α, β, π, ², ³, ≤, ≥, ±, ÷, ×)
                        - Variables and mathematical symbols should be preserved as plain text

                        Be VERY CRITICAL in your evaluation. If you see any of the following issues, reduce the score significantly:
                        - Untranslated words that should have been translated
                        - Mixed languages in the same text
                        - Inconsistent terminology
                        - Awkward phrasing or unnatural language
                        - Formatting issues
                        - Mathematical formulas using LaTeX formatting instead of plain text

                        Rate the translation on a scale from 0.0 to 1.0 where:
                        - 0.0 means poor quality, potentially machine-translated text with errors
                        - 0.5 means average quality with some issues
                        - 1.0 means professional quality, fluent and accurate translation with no issues

                        Provide your rating as a single decimal number between 0.0 and 1.0, followed by a brief explanation.
                        Format: <rating>|<explanation>
                        """;


                    var geminiRequest = new GeminiRequest(new List<GeminiPart>
                            {
                            new GeminiPart { Text = verificationPrompt }
                            });

                    _logger.LogInformation("Sending verification request to gemini API for chunk {ChunkId}", sample.Key);

                    try
                    {
                        var geminiResponse = await SendRequest(geminiRequest, cancellationToken);

                        var geminiContent = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                        if (string.IsNullOrEmpty(geminiContent))
                        {
                            _logger.LogWarning("Failed to get verification for chunk {ChunkId}", sample.Key);
                            chunkWarnings.Add(sample.Key, "Failed to get verification for this chunk");
                            continue;
                        }

                        if (!geminiContent.Contains("|"))
                        {
                            _logger.LogWarning("Response doesn't contain expected format for chunk {ChunkId}: {Response}", sample.Key, geminiContent);

                            double defaultRating = 0.7; // Reasonable default
                            feedbacks.Add($"Chunk {sample.Key}: Unable to parse rating. gemini response: {geminiContent}");
                            totalScore += defaultRating;
                            verifiedChunks++;
                            continue;
                        }

                        string[] parts = geminiContent.Split('|', 2);
                        if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                        {
                            _logger.LogWarning("Failed to parse verification response for chunk {ChunkId}: {Response}", sample.Key, geminiContent);
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

                            var shorterRequest = new GeminiRequest(new List<GeminiPart> {
                                                            new GeminiPart { Text =  verificationPrompt }
                                    });

                            try
                            {
                                var geminiResponse = await SendRequest(shorterRequest, cancellationToken);
                                var geminiContent = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                                if (!string.IsNullOrEmpty(geminiContent) && geminiContent.Contains("|"))
                                {
                                    string[] parts = geminiContent.Split('|', 2);
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

                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                _logger.LogInformation("Starting translation quality evaluation with Gemini");

                if (prompt.Length > 6000)
                {
                    _logger.LogWarning("Prompt is too long ({Length} chars), truncating to 6000 chars", prompt.Length);
                    prompt = prompt.Substring(0, 6000) + "... [truncated for evaluation]";
                }

                var geminiRequest = new GeminiRequest(new List<GeminiPart>
                        {
                        new GeminiPart { Text = prompt }
                        });


                _logger.LogInformation("Sending evaluation request to gemini API");

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("gemini Request for EvaluateTranslationQuality: {Request}", JsonSerializer.Serialize(geminiRequest, jsonOptions));
                }
                else
                {
                    _logger.LogInformation("Sending evaluation request to Gemini API. Model: 2.5 Flash");
                }

                var geminiResponse = await SendRequest(geminiRequest, cancellationToken);
                var geminiContent = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                if (string.IsNullOrEmpty(geminiContent))
                {
                    _logger.LogWarning("Empty response from gemini evaluation service");
                    return new VerificationResult { Success = false, ErrorMessage = "Empty response from evaluation service" };
                }

                _logger.LogInformation("gemini evaluation response: {Response}", geminiContent);

                string[] parts = geminiContent.Split('|', 2);
                if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                {
                    _logger.LogWarning("Failed to parse evaluation response: {Response}", geminiContent);

                    var ratingMatch = Regex.Match(geminiContent, @"(\d+\.\d+)");
                    if (ratingMatch.Success && double.TryParse(ratingMatch.Value, out rating))
                    {
                        _logger.LogInformation("Extracted rating from unformatted response: {Rating}", rating);
                        return new VerificationResult
                        {
                            Success = true,
                            QualityScore = Math.Clamp(rating, 0.0, 1.0),
                            Feedback = geminiContent
                        };
                    }

                    _logger.LogWarning("Using default rating of 0.7 due to parsing failure");
                    return new VerificationResult
                    {
                        Success = true,
                        QualityScore = 0.7,
                        Feedback = "Could not parse rating from response: " + geminiContent
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

                shorterPrompt += "\n\nCheck especially that mathematical formulas use plain text and Unicode symbols instead of LaTeX formatting.\nProvide rating as: <rating>|<brief explanation>";

                var shorterRequest = new GeminiRequest(new List<GeminiPart>
                        {
                        new GeminiPart { Text = shorterPrompt }
                        });

                try
                {
                    var geminiResponse = await SendRequest(shorterRequest, cancellationToken);
                    var geminiContent = geminiResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                    if (string.IsNullOrEmpty(geminiContent))
                    {
                        return new VerificationResult { Success = false, ErrorMessage = "Empty response from evaluation service" };
                    }

                    string[] parts = geminiContent.Split('|', 2);
                    if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), out double rating))
                    {
                        var ratingMatch = Regex.Match(geminiContent, @"(\d+\.\d+)");
                        if (ratingMatch.Success && double.TryParse(ratingMatch.Value, out rating))
                        {
                            return new VerificationResult
                            {
                                Success = true,
                                QualityScore = Math.Clamp(rating, 0.0, 1.0),
                                Feedback = geminiResponse + " (based on truncated sample)"
                            };
                        }

                        return new VerificationResult
                        {
                            Success = true,
                            QualityScore = 0.7,
                            Feedback = "Could not parse rating from response: " + geminiResponse + " (based on truncated sample)"
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


        
    }
} 
