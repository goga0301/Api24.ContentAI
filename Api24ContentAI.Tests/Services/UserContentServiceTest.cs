using Moq;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Entities;
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
        public async Task BasicMessageShouldUseCacheWhenCacheHasValue()
        {
            BasicMessageRequest request = new BasicMessageRequest { Message = "Test message" };
            CancellationToken cancellationToken = CancellationToken.None;
            string cacheKey = $"basic_message_{request.Message.GetHashCode()}";

            CopyrightAIResponse expectedResponse = new CopyrightAIResponse { Text = "Cached response" };

            _ = _mockCacheService
                .Setup(x => x.GetOrCreateAsync(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResponse);

            CopyrightAIResponse result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal(expectedResponse.Text, result.Text);
            _mockCacheService.Verify(
                x => x.GetOrCreateAsync(
                    It.Is<string>(k => k == cacheKey),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task BasicMessageShouldCallClaudeAndCacheResultWhenCacheEmpty()
        {
            BasicMessageRequest request = new BasicMessageRequest { Message = "Test message" };
            CancellationToken cancellationToken = CancellationToken.None;
            string cacheKey = $"basic_message_{request.Message.GetHashCode()}";

            CopyrightAIResponse capturedCacheValue = null;
            Func<Task<CopyrightAIResponse>> capturedCacheValueFactory = null;

            _ = _mockCacheService
                .Setup(x => x.GetOrCreateAsync(
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

            List<Content> claudeResponseContent =
            [
                new Content { Text = "Claude response text." }
            ];
            ClaudeResponse claudeResponse = new ClaudeResponse { Content = claudeResponseContent };

            _ = _mockClaudeService
                .Setup(x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);

            CopyrightAIResponse result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal("Claude response text.", result.Text);

            _mockCacheService.Verify(
                x => x.GetOrCreateAsync(
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
        public async Task BasicMessageShouldProcessClaudeResponseWhenCacheMiss()
        {
            BasicMessageRequest request = new BasicMessageRequest { Message = "Test message" };
            CancellationToken cancellationToken = CancellationToken.None;

            string claudeResponseText = "First sentence. Second sentence. Third sentence.";
            List<Content> claudeResponseContent =
            [
                new Content { Text = claudeResponseText }
            ];
            ClaudeResponse claudeResponse = new ClaudeResponse { Content = claudeResponseContent };

            _ = _mockClaudeService
                .Setup(static x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);

            _ = _mockCacheService
                .Setup(static x => x.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<CopyrightAIResponse>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(static (string key, Func<Task<CopyrightAIResponse>> factory, TimeSpan? expiration, CancellationToken token) =>
                {
                    return factory().Result;
                });

            CopyrightAIResponse result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal("First sentence. Second sentence. Third sentence.", result.Text);
        }

        [Fact]
        public async Task ChunkedTranslateShouldNotUseCacheDirectly()
        {
            UserTranslateRequest request = new UserTranslateRequest
            {
                Description = "Test text to translate",
                LanguageId = 1,
                SourceLanguageId = 2,
                IsPdf = false
            };
            string userId = "test-user-id";
            CancellationToken cancellationToken = CancellationToken.None;

            LanguageModel language = new LanguageModel { Id = 1, Name = "Georgian" };
            _ = _mockLanguageService
                .Setup(x => x.GetById(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(language);

            LanguageModel sourceLanguage = new LanguageModel { Id = 2, Name = "English" };
            _ = _mockLanguageService
                .Setup(x => x.GetById(2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sourceLanguage);

            User user = new User
            {
                Id = userId,
                UserBalance = new UserBalance { Balance = 100 }
            };
            _ = _mockUserRepository
                .Setup(x => x.GetById(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            List<Content> claudeResponseContent =
            [
                new Content { Text = "<translation>Translated text</translation>" }
            ];
            ClaudeResponse claudeResponse = new ClaudeResponse { Content = claudeResponseContent };

            _ = _mockClaudeService
                .Setup(x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claudeResponse);

            _ = _mockCacheService
                .Setup(x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _ = _mockCacheService
                .Setup(x => x.GetAsync<List<string>>(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(["test_key"]);

            _ = await _userContentService.ChunkedTranslate(request, userId, cancellationToken);

            _mockCacheService.Verify(
                x => x.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<It.IsAnyType>>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            _mockCacheService.Verify(
                x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);

            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task StoreChunkedTranslationResponsesShouldStoreAllResponses()
        {
            string translationId = "test-translation-id";
            CancellationToken cancellationToken = CancellationToken.None;

            List<ClaudeResponse> responses =
            [
                new ClaudeResponse { Content = [new Content { Text = "<translation>Chunk 1</translation>" }] },
                new ClaudeResponse { Content = [new Content { Text = "<translation>Chunk 2</translation>" }] },
                new ClaudeResponse { Content = [new Content { Text = "<translation>Chunk 3</translation>" }] },
                new ClaudeResponse { Content = [new Content { Text = "<translation>Chunk 4</translation>" }] }
            ];

            Dictionary<string, object> storedItems = [];
            List<string> storedKeys = [];

            _ = _mockCacheService
                .Setup(x => x.SetAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, object, TimeSpan?, CancellationToken>((key, value, expiry, token) =>
                {
                    storedItems[key] = value;
                    storedKeys.Add(key);
                })
                .Returns(Task.CompletedTask);

            System.Reflection.MethodInfo? methodInfo = typeof(UserContentService).GetMethod(
                "StoreChunkedTranslationResponses",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            List<string> result = await (Task<List<string>>)methodInfo.Invoke(_userContentService,[responses, translationId, cancellationToken]);

            Assert.Equal(responses.Count, result.Count);

            Assert.Equal(responses.Count + 1, storedItems.Count); // +1 for the keys list

            string keysListKey = $"claude_response_keys_{translationId}";
            Assert.Contains(keysListKey, storedItems.Keys);

            List<string>? storedKeysList = storedItems[keysListKey] as List<string>;
            Assert.NotNull(storedKeysList);
            Assert.Equal(responses.Count, storedKeysList.Count);

            foreach (string key in result)
            {
                Assert.StartsWith($"claude_response_{translationId}_chunk_", key);
            }

            for (int i = 0; i < responses.Count; i++)
            {
                string? chunkKey = storedKeys.FirstOrDefault(k => k.Contains($"_chunk_{i}_"));
                Assert.NotNull(chunkKey);

                string? storedValue = storedItems[chunkKey] as string;
                Assert.NotNull(storedValue);

                ClaudeResponse? deserializedResponse = JsonSerializer.Deserialize<ClaudeResponse>(storedValue);
                Assert.NotNull(deserializedResponse);
                Assert.Equal(responses[i].Content[0].Text, deserializedResponse.Content[0].Text);
            }
        }

    }
}

