using Moq;
using Xunit;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace Api24ContentAI.Tests.Services
{
    public class UserContentServiceTests
    {
        private readonly Mock<IClaudeService> _mockClaudeService;
        private readonly Mock<ICacheService> _mockCacheService;
        private readonly Mock<IUserRequestLogService> _mockRequestLogService;
        private readonly Mock<IProductCategoryService> _mockProductCategoryService;
        private readonly Mock<ILanguageService> _mockLanguageService;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly UserContentService _userContentService;

        public UserContentServiceTests()
        {
            _mockClaudeService = new Mock<IClaudeService>();
            _mockCacheService = new Mock<ICacheService>();
            _mockRequestLogService = new Mock<IUserRequestLogService>();
            _mockProductCategoryService = new Mock<IProductCategoryService>();
            _mockLanguageService = new Mock<ILanguageService>();
            _mockUserRepository = new Mock<IUserRepository>();

            _userContentService = new UserContentService(
                _mockClaudeService.Object,
                _mockCacheService.Object,
                _mockRequestLogService.Object,
                _mockProductCategoryService.Object,
                _mockLanguageService.Object,
                _mockUserRepository.Object);
        }

          [Fact]
        public async Task BasicMessage_ShouldUseCache_WhenCacheHasValue()
        {
            // Arrange
            var request = new BasicMessageRequest { Message = "Test message" };
            var cancellationToken = CancellationToken.None;
            var cacheKey = $"basic_message_{request.Message.GetHashCode()}";
            
            var expectedResponse = new CopyrightAIResponse { Text = "Cached response" };
            
            _mockCacheService
                .Setup(x => x.GetOrCreateAsync<CopyrightAIResponse>(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _userContentService.BasicMessage(request, cancellationToken);

            // Assert
            Assert.Equal(expectedResponse.Text, result.Text);
            _mockCacheService.Verify(
                x => x.GetOrCreateAsync<CopyrightAIResponse>(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            
            // Verify Claude service was not called
            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

         [Fact]
        public async Task BasicMessage_ShouldCallClaudeAndCacheResult_WhenCacheEmpty()
        {
            var request = new BasicMessageRequest { Message = "Test message" };
            var cancellationToken = CancellationToken.None;
            var cacheKey = $"basic_message_{request.Message.GetHashCode()}";
            
            CopyrightAIResponse capturedCacheValue = null;
            Func<Task<CopyrightAIResponse>> capturedCacheValueFactory = null;
            
            _mockCacheService
                .Setup(x => x.GetOrCreateAsync<CopyrightAIResponse>(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, Func<Task<CopyrightAIResponse>>, TimeSpan?, CancellationToken>((key, factory, expiration, token) => 
                {
                    capturedCacheValueFactory = factory;
                })
                .ReturnsAsync((string key, Func<Task<CopyrightAIResponse>> factory, TimeSpan? expiration, CancellationToken token) => 
                {
                    return factory().Result;
                });
            
            var claudeResponseContent = new List<Content>
            {
                new Content { Text = "Claude response text." }
            };
            var claudeResponse = new ClaudeResponse { Content = claudeResponseContent };
            
            _mockClaudeService
                .Setup(x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);

            var result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal("Claude response text.", result.Text);
            
            _mockCacheService.Verify(
                x => x.GetOrCreateAsync<CopyrightAIResponse>(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            
            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task BasicMessage_ShouldProcessClaudeResponse_WhenCacheMiss()
        {
            var request = new BasicMessageRequest { Message = "Test message" };
            var cancellationToken = CancellationToken.None;
            
            var claudeResponseText = "First sentence. Second sentence. Third sentence.";
            var claudeResponseContent = new List<Content>
            {
                new Content { Text = claudeResponseText }
            };
            var claudeResponse = new ClaudeResponse { Content = claudeResponseContent };
            
            _mockClaudeService
                .Setup(x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);
            
            _mockCacheService
                .Setup(x => x.GetOrCreateAsync<CopyrightAIResponse>(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, Func<Task<CopyrightAIResponse>> factory, TimeSpan? expiration, CancellationToken token) => 
                {
                    return factory().Result;
                });

            var result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal("First sentence. Second sentence. Third sentence.", result.Text);
        }

        [Fact]
        public async Task ChunkedTranslate_ShouldNotUseCacheDirectly()
        {
            var request = new UserTranslateRequest
            {
                Description = "Test text to translate",
                LanguageId = 1,
                SourceLanguageId = 2,
                IsPdf = false
            };
            var userId = "test-user-id";
            var cancellationToken = CancellationToken.None;
            
            var language = new LanguageModel { Id = 1, Name = "Georgian" };
            _mockLanguageService
                .Setup(x => x.GetById(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(language);
            
            var sourceLanguage = new LanguageModel { Id = 2, Name = "English" };
            _mockLanguageService
                .Setup(x => x.GetById(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sourceLanguage);
            
            var user = new User
            {
                Id = userId,
                UserBalance = new UserBalance { Balance = 100 }
            };
            _mockUserRepository
                .Setup(x => x.GetById(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            
            var claudeResponseContent = new List<Content>
            {
                new Content { Text = "<translation>Translated text</translation>" }
            };
            var claudeResponse = new ClaudeResponse { Content = claudeResponseContent };
            
            _mockClaudeService
                .Setup(x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);
            
            _mockCacheService
                .Setup(x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            
            _mockCacheService
                .Setup(x => x.GetAsync<List<string>>(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(["test_key"]);
            
            await _userContentService.ChunkedTranslate(request, userId, cancellationToken);
            
            // Assert
            // Verify that GetOrCreateAsync was never called
            _mockCacheService.Verify(
                x => x.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<It.IsAnyType>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            
            // Verify that SetAsync was called at least once
            _mockCacheService.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
            
            // Verify Claude service was called
            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task StoreChunkedTranslationResponses_ShouldStoreAllResponses()
        {
            // Arrange
            var translationId = "test-translation-id";
            var cancellationToken = CancellationToken.None;
            
            // Create a list of Claude responses to store
            var responses = new List<ClaudeResponse>
            {
                new ClaudeResponse { Content = new List<Content> { new Content { Text = "<translation>Chunk 1</translation>" } } },
                new ClaudeResponse { Content = new List<Content> { new Content { Text = "<translation>Chunk 2</translation>" } } },
                new ClaudeResponse { Content = new List<Content> { new Content { Text = "<translation>Chunk 3</translation>" } } },
                new ClaudeResponse { Content = new List<Content> { new Content { Text = "<translation>Chunk 4</translation>" } } }
            };
            
            // Track the keys and values stored in the cache
            var storedItems = new Dictionary<string, object>();
            var storedKeys = new List<string>();
            
            // Setup cache service to track stored items
            _mockCacheService
                .Setup(x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan?>(), // Changed from TimeSpan to TimeSpan?
                    It.IsAny<CancellationToken>()))
                .Callback<string, object, TimeSpan?, CancellationToken>((key, value, expiry, token) => 
                {
                    // Store the key and value
                    storedItems[key] = value;
                    storedKeys.Add(key);
                })
                .Returns(Task.CompletedTask);
            
            // Use reflection to access the private method
            var methodInfo = typeof(UserContentService).GetMethod(
                "StoreChunkedTranslationResponses", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Act
            var result = await (Task<List<string>>)methodInfo.Invoke(
                _userContentService, 
                new object[] { responses, translationId, cancellationToken });
            
            // Assert
            // Check that the method returned the correct number of keys
            Assert.Equal(responses.Count, result.Count);
            
            // Check that all responses were stored in the cache
            Assert.Equal(responses.Count + 1, storedItems.Count); // +1 for the keys list
            
            // Check that the keys list was stored with the expected key
            string keysListKey = $"claude_response_keys_{translationId}";
            Assert.Contains(keysListKey, storedItems.Keys);
            
            // Check that the keys list contains all the response keys
            var storedKeysList = storedItems[keysListKey] as List<string>;
            Assert.NotNull(storedKeysList);
            Assert.Equal(responses.Count, storedKeysList.Count);
            
            // Check that each response key starts with the expected prefix
            foreach (var key in result)
            {
                Assert.StartsWith($"claude_response_{translationId}_chunk_", key);
            }
            
            // Check that each response was stored with the correct format
            for (int i = 0; i < responses.Count; i++)
            {
                // Find the key for this chunk
                var chunkKey = storedKeys.FirstOrDefault(k => k.Contains($"_chunk_{i}_"));
                Assert.NotNull(chunkKey);
                
                // Check that the value is a serialized JSON string
                var storedValue = storedItems[chunkKey] as string;
                Assert.NotNull(storedValue);
                
                // Verify it's valid JSON that can be deserialized back to a ClaudeResponse
                var deserializedResponse = JsonSerializer.Deserialize<ClaudeResponse>(storedValue);
                Assert.NotNull(deserializedResponse);
                Assert.Equal(responses[i].Content[0].Text, deserializedResponse.Content[0].Text);
            }
        }

    }
}

