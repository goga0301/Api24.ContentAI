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
                
                // Check for unsupported file types and convert them using screenshot service
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
    }
} 
