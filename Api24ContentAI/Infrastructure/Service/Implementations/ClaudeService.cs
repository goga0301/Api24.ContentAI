using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ClaudeService(HttpClient httpClient) : IClaudeService
    {
        private readonly HttpClient _httpClient = httpClient;
        private const string Messages = "messages";

        public async Task<ClaudeResponse> SendRequest(ClaudeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync(Messages, request, cancellationToken);

                string str = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<ClaudeResponse>(str);
            }
            catch (Exception ex)
            {
                throw new Exception("Error in integration service ", ex.InnerException);
            }
        }

        public async Task<ClaudeResponse> SendRequestWithFile(ClaudeRequestWithFile request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await _httpClient.PostAsJsonAsync(Messages, request, cancellationToken);

            string str = await response.Content.ReadAsStringAsync(cancellationToken);
            ClaudeResponse result = JsonSerializer.Deserialize<ClaudeResponse>(str);
            return result.Content == null ? throw new Exception(str) : result;
        }
    }
}
