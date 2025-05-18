using Api24ContentAI.Domain.Models;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Api24ContentAI.Tests.UnitTests.Services
{
    public class GptServiceTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ILogger<GptService>> _mockLogger;
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly HttpClient _httpClient;
        private readonly GptService _gptService;

        public GptServiceTests()
        {
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_mockHttpMessageHandler.Object)
            {
                BaseAddress = new Uri("https://api.openai.com/")
            };

            _mockConfiguration = new Mock<IConfiguration>();
            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns("test-api-key");
            _mockConfiguration.Setup(x => x.GetSection("Security:OpenAIApiKey")).Returns(configSection.Object);

            var modelSection = new Mock<IConfigurationSection>();
            modelSection.Setup(x => x.Value).Returns("gpt-4");
            _mockConfiguration.Setup(x => x.GetSection("OpenAI:DefaultModel")).Returns(modelSection.Object);

            _mockLogger = new Mock<ILogger<GptService>>();

            _gptService = new GptService(_httpClient, _mockConfiguration.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task VerifyResponseQuality_ShouldReturnSuccessResult_WhenApiReturnsValidResponse()
        {
            var request = new ClaudeRequest("Test prompt");
            var response = new ClaudeResponse
            {
                Content = new List<Content>
                {
                    new Content { Text = "This is a test response from Claude." }
                }
            };

            var httpResponse = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{
                    ""id"": ""chatcmpl-123"",
                    ""object"": ""chat.completion"",
                    ""created"": 1677652288,
                    ""choices"": [{
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""0.85|The response is well-structured, relevant to the request, and provides accurate information.""
                        },
                        ""finish_reason"": ""stop""
                    }]
                }")
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(httpResponse);

            var result = await _gptService.VerifyResponseQuality(request, response, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(0.85, result.QualityScore);
            Assert.Equal("The response is well-structured, relevant to the request, and provides accurate information.", result.Feedback);
        }

        [Fact]
        public async Task VerifyResponseQuality_ShouldReturnFailureResult_WhenResponseIsEmpty()
        {
            var request = new ClaudeRequest("Test prompt");
            var response = new ClaudeResponse
            {
                Content = new List<Content>
                {
                    new Content { Text = "" }
                }
            };

            var result = await _gptService.VerifyResponseQuality(request, response, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("Empty response content", result.ErrorMessage);
        }

        [Fact]
        public async Task VerifyTranslationBatch_ShouldReturnAverageScore_WhenApiReturnsValidResponses()
        {
            var translations = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(1, "This is the first translated paragraph."),
                new KeyValuePair<int, string>(2, "This is the second translated paragraph."),
                new KeyValuePair<int, string>(3, "This is the third translated paragraph.")
            };

            var httpResponse = new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{
                    ""id"": ""chatcmpl-123"",
                    ""object"": ""chat.completion"",
                    ""created"": 1677652288,
                    ""choices"": [{
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""0.9|The translation is fluent and natural, with consistent terminology and no obvious errors.""
                        },
                        ""finish_reason"": ""stop""
                    }]
                }")
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(httpResponse);

            var result = await _gptService.VerifyTranslationBatch(translations, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(0.9, result.QualityScore);
            Assert.Contains("The translation is fluent and natural", result.Feedback);
            Assert.Equal(3, result.VerifiedChunks);
        }
    }
}