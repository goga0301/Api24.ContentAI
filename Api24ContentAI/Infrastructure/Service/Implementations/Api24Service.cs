using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class Api24Service : IApi24Service
    {
        public const string Categories = "basedata/categories";
        private const string CategoriesCacheKey = "api24_categories";

        private readonly HttpClient _httpClient;

        public Api24Service(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task<List<CategoryResponse>> GetCategories(CancellationToken cancellationToken)
        {
            return await _httpClient.GetFromJsonAsync<List<CategoryResponse>>(Categories, cancellationToken);
        }
    }
}
