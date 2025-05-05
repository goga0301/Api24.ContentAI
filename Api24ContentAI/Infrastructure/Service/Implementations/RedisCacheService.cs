using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Caching.Distributed;
using System;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedCacheEntryOptions _defaultOptions;
        private readonly ILogger<RedisCacheService> _logger;

        private static readonly Action<ILogger, string, Exception?> _cacheHitLog =
           LoggerMessage.Define<string>(LogLevel.Information, new EventId(1001, "CacheHit"), "Cache HIT for key: {Key}");

        private static readonly Action<ILogger, string, Exception?> _cacheMissLog =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(1002, "CacheMiss"), "Cache MISS for key: {Key}");

        private static readonly Action<ILogger, string, Exception> _cacheErrorLog =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(1003, "CacheError"), "Cache ERROR for key: {Key}");


        public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
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

            if (cached == null)
            {
                _cacheMissLog(_logger, key, null);
                return default;
            }

            _cacheHitLog(_logger, key, null);


            try
            {
                return JsonSerializer.Deserialize<T>(cached);
            }
            catch (JsonException ex)
            {
                _cacheErrorLog(_logger, key, ex);
                //  remove corrupted cache entry
                await _cache.RemoveAsync(key, cancellationToken);
                return default;
            }
        }


        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        {
            DistributedCacheEntryOptions options = new()
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
