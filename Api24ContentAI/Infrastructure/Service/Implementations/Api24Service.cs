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
        private readonly ICacheService _cacheService;

        public Api24Service(HttpClient httpClient, ICacheService cacheService)
        {
            _httpClient = httpClient;
            _cacheService = cacheService;
        }


        public async Task<List<CategoryResponse>> GetCategories(CancellationToken cancellationToken)
        {
            return await _cacheService.GetOrCreateAsync(
                    CategoriesCacheKey,
                    async () => await _httpClient.GetFromJsonAsync<List<CategoryResponse>>(Categories, cancellationToken),
                    TimeSpan.FromHours(24),
                    cancellationToken
                    );
        }
    }
}
