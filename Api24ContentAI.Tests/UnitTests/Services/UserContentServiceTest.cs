using System.Collections.Generic;
using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Api24ContentAI.Tests.UnitTests.Services
{
    public class UserContentServiceTest
    {
        private readonly Mock<IClaudeService> _mockClaudeService;
        private readonly Mock<IUserRequestLogService> _mockUserRequestLogService;
        private readonly Mock<IProductCategoryService> _mockProductCategoryService;
        private readonly Mock<ILanguageService> _mockLanguageService;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ILogger<UserContentService>> _mockLogger;
        private readonly UserContentService _userContentService;

        public UserContentServiceTest()
        {
            _mockClaudeService = new Mock<IClaudeService>();
            _mockUserRequestLogService = new Mock<IUserRequestLogService>();
            _mockProductCategoryService = new Mock<IProductCategoryService>();
            _mockLanguageService = new Mock<ILanguageService>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockLogger = new Mock<ILogger<UserContentService>>();

            _userContentService = new UserContentService(
                _mockClaudeService.Object,
                _mockUserRequestLogService.Object,
                _mockProductCategoryService.Object,
                _mockLanguageService.Object,
                _mockUserRepository.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task BasicMessageShouldCallClaudeService()
        {
            BasicMessageRequest request = new BasicMessageRequest { Message = "Test message" };
            CancellationToken cancellationToken = CancellationToken.None;

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
            _mockClaudeService.Verify(
                x => x.SendRequestWithFile(It.IsAny<ClaudeRequestWithFile>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task BasicMessageShouldProcessClaudeResponse()
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

            CopyrightAIResponse result = await _userContentService.BasicMessage(request, cancellationToken);

            Assert.Equal("First sentence. Second sentence. Third sentence.", result.Text);
        }
    }
}
