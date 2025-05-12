using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Api24ContentAI.Tests.IntegrationTests.Services
{
    public class RedisCacheIntegrationTest
    {
        private readonly ICacheService _cacheService;
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly string _redisHost = "localhost:6379";
        
        public RedisCacheIntegrationTest()
        {
            var services = new ServiceCollection();
            
            _redisConnection = ConnectionMultiplexer.Connect(_redisHost);
            
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = _redisHost;
                options.InstanceName = "TestInstance:";
            });
            
            services.AddSingleton<ILogger<RedisCacheService>>(new NullLogger<RedisCacheService>());
            
            var serviceProvider = services.BuildServiceProvider();
            
            var distributedCache = serviceProvider.GetRequiredService<IDistributedCache>();
            var logger = serviceProvider.GetRequiredService<ILogger<RedisCacheService>>();
            
            _cacheService = new RedisCacheService(distributedCache, logger);
        }

        [Fact]
        public void Redis_ShouldBeConnected()
        {
            Assert.True(_redisConnection.IsConnected, "Redis connection should be active");
            
            var db = _redisConnection.GetDatabase();
            var pingResult = db.Execute("PING");
            
            Assert.Equal("PONG", pingResult.ToString());
        }

        [Fact]
        public async Task RedisCacheService_ShouldStoreAndRetrieveValues()
        {
            string testKey = $"test_key_{Guid.NewGuid()}";
            string testValue = "Test value " + DateTime.UtcNow;
            
            try
            {
                await _cacheService.SetAsync(testKey, testValue, TimeSpan.FromMinutes(5));
                
                string retrievedValue = await _cacheService.GetAsync<string>(testKey);
                
                Assert.Equal(testValue, retrievedValue);
                
                var db = _redisConnection.GetDatabase();
                bool keyExists = await db.KeyExistsAsync($"TestInstance:{testKey}");
                Assert.True(keyExists, "Key should exist in Redis");
            }
            finally
            {
                await _cacheService.RemoveAsync(testKey);
            }
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldUseCache()
        {
            string testKey = $"test_key_{Guid.NewGuid()}";
            int factoryCallCount = 0;
            
            try
            {
                string result1 = await _cacheService.GetOrCreateAsync(
                    testKey,
                    async () => {
                        factoryCallCount++;
                        return $"Generated value {factoryCallCount}";
                    },
                    TimeSpan.FromMinutes(5));
                    
                string result2 = await _cacheService.GetOrCreateAsync(
                    testKey,
                    async () => {
                        factoryCallCount++;
                        return $"Generated value {factoryCallCount}";
                    },
                    TimeSpan.FromMinutes(5));
                
                Assert.Equal(1, factoryCallCount);
                Assert.Equal("Generated value 1", result1);
                Assert.Equal("Generated value 1", result2);
                
                var db = _redisConnection.GetDatabase();
                bool keyExists = await db.KeyExistsAsync($"TestInstance:{testKey}");
                Assert.True(keyExists, "Key should exist in Redis");
            }
            finally
            {
                await _cacheService.RemoveAsync(testKey);
            }
        }
    }
}
