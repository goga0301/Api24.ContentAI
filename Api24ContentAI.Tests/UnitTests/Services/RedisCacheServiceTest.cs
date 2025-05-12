using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Api24ContentAI.Tests.UnitTests.Services
{
    public class RedisCacheServiceTest
    {
        private readonly Mock<IDistributedCache> _mockCache;
        private readonly Mock<ILogger<RedisCacheService>> _mockLogger;
        private readonly RedisCacheService _cacheService;

        public RedisCacheServiceTest()
        {
            _mockCache = new Mock<IDistributedCache>();
            _mockLogger = new Mock<ILogger<RedisCacheService>>();
            _cacheService = new RedisCacheService(_mockCache.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldReturnCachedValue_WhenKeyExists()
        {
            string key = "test_key";
            TestObject expectedValue = new TestObject { Id = 1, Name = "Test" };
            byte[] serializedValue = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            _mockCache
                .Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(serializedValue);

            TestObject result = await _cacheService.GetAsync<TestObject>(key);

            Assert.NotNull(result);
            Assert.Equal(expectedValue.Id, result.Id);
            Assert.Equal(expectedValue.Name, result.Name);
            
            _mockCache.Verify(x => x.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldCallFactory_WhenKeyDoesNotExist()
        {
            string key = "test_key";
            TestObject expectedValue = new TestObject { Id = 1, Name = "Test" };
            bool factoryCalled = false;

            _mockCache
                .Setup(x => x.GetAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[])null);

            _mockCache
                .Setup(x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<DistributedCacheEntryOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            TestObject result = await _cacheService.GetOrCreateAsync(
                key,
                async () => {
                    factoryCalled = true;
                    return expectedValue;
                });

            Assert.True(factoryCalled);
            Assert.NotNull(result);
            Assert.Equal(expectedValue.Id, result.Id);
            Assert.Equal(expectedValue.Name, result.Name);
            
            _mockCache.Verify(x => x.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
            _mockCache.Verify(x => x.SetAsync(
                key,
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}