using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedCacheEntryOptions _defaultOptions;

        public RedisCacheService(IDistributedCache cache)
        {
            _cache = cache;
            _defaultOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            };
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            T cached = await GetAsync<T>(key, cancellationToken);
            if (cached != null)
            {
                return cached;
            }

            T value = await factory();
            await SetAsync(key, value, expiration, cancellationToken);
            return value;
        }

        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            byte[] cached = await _cache.GetAsync(key, cancellationToken);

            return cached == null ? default : JsonSerializer.Deserialize<T>(cached);
        }


        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            DistributedCacheEntryOptions options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? _defaultOptions.AbsoluteExpirationRelativeToNow
            };

            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(value);
            await _cache.SetAsync(key, serialized, options, cancellationToken);
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }

    }
}
