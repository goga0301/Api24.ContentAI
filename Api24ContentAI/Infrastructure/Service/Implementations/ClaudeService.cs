﻿using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ClaudeService : IClaudeService
    {
        private readonly HttpClient _httpClient;
        private const string Messages = "messages";
        public ClaudeService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ClaudeResponse> SendRequest(ClaudeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(Messages, request, cancellationToken);

                var str = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<ClaudeResponse>(str);
            }
            catch(Exception ex)
            {

            }
            return null;
        }
    }
}
