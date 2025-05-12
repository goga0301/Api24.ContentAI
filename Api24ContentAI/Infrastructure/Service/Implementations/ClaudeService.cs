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

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ClaudeService(HttpClient httpClient, IConfiguration configuration) : IClaudeService
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly HttpClient _httpClient = httpClient;
        private const string Messages = "messages";

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
                throw new Exception("Error in integration service ", ex);
            }
        }

        public async Task<ClaudeResponse> SendRequestWithFile(ClaudeRequestWithFile request, CancellationToken cancellationToken)
        {
            try
            {
                string apiKey = _configuration.GetSection("Security:ClaudeApiKey").Value;

                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Claude API key is missing in configuration.");
                }

                if (_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("x-api-key");
                }
                _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

                if (_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("anthropic-version");
                }
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                if (_httpClient.DefaultRequestHeaders.Contains("anthropic-beta"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("anthropic-beta");
                }
                _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-3-5-sonnet-2024-07-15");

                JsonSerializerOptions jsonOptions = new()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = false
                };

                foreach (MessageWithFile message in request.Messages)
                {
                    foreach (ContentFile contentItem in message.Content)
                    {
                        if (contentItem.Type == "image" && contentItem.Source != null)
                        {
                            if (string.IsNullOrEmpty(contentItem.Source.Data))
                            {
                                throw new Exception("Image data is empty");
                            }

                            // Ensure media_type is properly set
                            if (string.IsNullOrEmpty(contentItem.Source.MediaType))
                            {
                                throw new Exception("Image media type is not specified");
                            }
                        }
                    }
                }

                string jsonContent = JsonSerializer.Serialize(request, jsonOptions);

                StringContent httpContent = new(jsonContent, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(Messages, httpContent, cancellationToken);

                string responseStr = await response.Content.ReadAsStringAsync(cancellationToken);
                // NOTE: remove this response
                Console.WriteLine("Response: " + responseStr);

                return !response.IsSuccessStatusCode
                    ? throw new Exception($"Claude API error: {responseStr}")
                    : JsonSerializer.Deserialize<ClaudeResponse>(responseStr, jsonOptions);
            }
            catch (Exception ex)
            {
                throw new Exception("Error in Claude integration service: " + ex.Message, ex);
            }
        }

        private void EnsureHeaders()
        {

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
                _httpClient.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-3-5-sonnet-2024-07-15");
            }

            if (!_httpClient.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            {
                _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            }
        }
    }
}
