using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Collections.Generic;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ClaudeService(HttpClient httpClient, IConfiguration configuration, ILogger<ClaudeService> logger) : IClaudeService
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly HttpClient _httpClient = httpClient;
        private const string Messages = "messages";
        private readonly ILogger<ClaudeService> _logger = logger;
        private bool _headersInitialized = false;

        public async Task<ClaudeResponse> SendRequest(ClaudeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                EnsureHeaders();
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync(Messages, request, cancellationToken);

                string str = await response.Content.ReadAsStringAsync(cancellationToken);
                return !response.IsSuccessStatusCode
                    ? throw new Exception($"Claude API error: {str}")
                    : JsonSerializer.Deserialize<ClaudeResponse>(str);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendRequest");
                throw new Exception("Error in integration service ", ex);
            }
        }

        public async Task<ClaudeResponse> SendRequestWithFile(ClaudeRequestWithFile request, CancellationToken cancellationToken)
        {
            try
            {
                EnsureHeaders();
                
                _logger.LogDebug("Sending request to Claude API with {Count} content items", 
                    request.Messages.SelectMany(m => m.Content).Count());
                
                bool hasImages = false;
                int totalImageDataSize = 0;
                
                foreach (MessageWithFile message in request.Messages)
                {
                    foreach (ContentFile contentItem in message.Content)
                    {
                        if (contentItem.Type == "image" && contentItem.Source != null)
                        {
                            hasImages = true;
                            totalImageDataSize += contentItem.Source.Data?.Length ?? 0;
                            
                            if (string.IsNullOrEmpty(contentItem.Source.Data))
                            {
                                _logger.LogError("Image data is empty");
                                throw new Exception("Image data is empty");
                            }

                            if (string.IsNullOrEmpty(contentItem.Source.MediaType))
                            {
                                _logger.LogError("Image media type is not specified");
                                throw new Exception("Image media type is not specified");
                            }
                            
                            _logger.LogDebug("Image content: MediaType={MediaType}, DataLength={DataLength}", 
                                contentItem.Source.MediaType, 
                                contentItem.Source.Data?.Length ?? 0);
                        }
                    }
                }

                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                };

                string jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                
                _logger.LogDebug("Claude API request size: {Size} bytes, has images: {HasImages}, total image data: {ImageDataSize}", 
                    jsonContent.Length, hasImages, totalImageDataSize);

                StringContent httpContent = new(jsonContent, Encoding.UTF8, "application/json");
                
                // Enhanced timeout logic considering both request size and image processing complexity
                TimeSpan timeout = DetermineTimeout(jsonContent.Length, hasImages, totalImageDataSize);
                
                using (var timeoutCts = new CancellationTokenSource(timeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    if (hasImages || jsonContent.Length > 500000) // 500KB
                    {
                        _logger.LogInformation("Request contains images or is large (Size: {Size} bytes, Images: {HasImages}, Image data: {ImageDataSize}), using extended timeout of {Timeout} seconds", 
                            jsonContent.Length, hasImages, totalImageDataSize, timeout.TotalSeconds);
                    }

                    var response = await _httpClient.PostAsync(Messages, httpContent, linkedCts.Token);

                    string responseStr = await response.Content.ReadAsStringAsync(cancellationToken);
                
                    _logger.LogDebug("Claude API response: {Response}", responseStr);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Claude API error: {StatusCode} - {Response}", 
                            response.StatusCode, responseStr);
                        throw new Exception($"Claude API error: {responseStr}");
                    }
                
                    return JsonSerializer.Deserialize<ClaudeResponse>(responseStr, jsonOptions);
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Claude API request was cancelled or timed out");
                throw new Exception("Claude API request timed out. The image processing is taking longer than expected. Please try again or use a smaller image.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Claude integration service");
                throw new Exception("Error in Claude integration service: " + ex.Message, ex);
            }
        }

        private static TimeSpan DetermineTimeout(int requestSize, bool hasImages, int totalImageDataSize)
        {
            TimeSpan baseTimeout = requestSize switch
            {
                > 5000000 => TimeSpan.FromMinutes(8),   // 5MB+ = 8 minutes
                > 2000000 => TimeSpan.FromMinutes(6),   // 2MB+ = 6 minutes  
                > 1000000 => TimeSpan.FromMinutes(4),   // 1MB+ = 4 minutes
                > 500000 => TimeSpan.FromMinutes(3),    // 500KB+ = 3 minutes
                _ => TimeSpan.FromMinutes(2)            // Default = 2 minutes
            };

            if (hasImages)
            {
                var imageProcessingTime = totalImageDataSize switch
                {
                    > 1000000 => TimeSpan.FromMinutes(3),   // 1MB+ image data = +3 min
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

        public async Task<ClaudeResponse> SendRequestWithCachedPrompt(ClaudeRequestWithFile request, CancellationToken cancellationToken)
        {
            return await SendRequestWithFile(request, cancellationToken);
        }

        public static List<SystemMessage> CreateCachedSystemPrompt(string promptText)
        {
            return new List<SystemMessage>
            {
                new SystemMessage
                {
                    Type = "text",
                    Text = promptText,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };
        }

        public static ContentFile CreateCacheableContent(string text, bool enableCaching = true)
        {
            return new ContentFile
            {
                Type = "text",
                Text = text,
                CacheControl = enableCaching ? new CacheControl { Type = "ephemeral" } : null
            };
        }

        private void EnsureHeaders()
        {
            if (_headersInitialized)
            {
                return;
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
            {
                string apiKey = _configuration.GetSection("Security:ClaudeApiKey").Value;
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Claude API key is missing in configuration.");
                }
                _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
            {
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-beta"))
            {
                _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,max-tokens-3-5-sonnet-2024-07-15");
            }

            if (!_httpClient.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            {
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
            
            _httpClient.Timeout = TimeSpan.FromMinutes(12);
            
            _headersInitialized = true;
        }
    }
}
