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
                
                var url = $"v1beta/models/gemini-2.0-flash-exp:generateContent?key={_apiKey}";
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
                
                var request = new GeminiRequest(parts);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                string jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                _logger.LogDebug("Gemini API request size: {Size} bytes", jsonContent.Length);

                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                TimeSpan timeout = jsonContent.Length switch
                {
                    > 5000000 => TimeSpan.FromSeconds(450), // 5MB+ = 7.5 minutes
                    > 2000000 => TimeSpan.FromSeconds(300), // 2MB+ = 5 minutes
                    > 1000000 => TimeSpan.FromSeconds(150), // 1MB+ = 2.5 minutes
                    _ => TimeSpan.FromSeconds(60)           // Default = 1 minute
                };

                if (jsonContent.Length > 1000000) // 1MB
                {
                    _logger.LogInformation("Request is large ({Size} bytes), using extended timeout of {Timeout} seconds", 
                        jsonContent.Length, timeout.TotalSeconds);
                    
                    using var requestClient = new HttpClient();
                    requestClient.Timeout = timeout;
                    requestClient.BaseAddress = _httpClient.BaseAddress;
                    
                    var url = $"v1beta/models/gemini-2.0-flash-exp:generateContent?key={_apiKey}";
                    var response = await requestClient.PostAsync(url, httpContent, cancellationToken);

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
                else
                {
                    var url = $"v1beta/models/gemini-2.0-flash-exp:generateContent?key={_apiKey}";
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Gemini integration service");
                throw new Exception("Error in Gemini integration service: " + ex.Message, ex);
            }
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
