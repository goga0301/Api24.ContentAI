using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/document-translation/chat")]
    [Authorize]
    public class DocumentTranslationChatController : ControllerBase
    {
        private readonly IDocumentTranslationChatService _chatService;
        private readonly ILogger<DocumentTranslationChatController> _logger;

        public DocumentTranslationChatController(
            IDocumentTranslationChatService chatService,
            ILogger<DocumentTranslationChatController> logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{chatId}")]
        public async Task<IActionResult> GetChat(
            string chatId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var userId = GetUserId();
                var chat = await _chatService.GetChat(chatId, cancellationToken);

                if (chat == null)
                {
                    return NotFound(new { Message = "Chat not found" });
                }

                return Ok(new
                {
                    Success = true,
                    Data = chat
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
                return StatusCode(500, "Internal server error while retrieving chat");
            }
        }

        [HttpGet("{chatId}/file")]
        public async Task<IActionResult> GetChatDocument(
                string chatId,
                CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var file = await _chatService.GetChatFile(chatId, cancellationToken);
            return File(file.DocumentData, file.ContentType, file.FileName);
        }

        [HttpPut("{chatId}/content")]
        public async Task<IActionResult> UpdateDocumentContent(
                string chatId,
                [FromBody] string newContent,
                CancellationToken cancellationToken
                )
        {
            var userId = GetUserId();
            var updated = await _chatService.UpdateChatFileContent(chatId, newContent, cancellationToken);

            if (!updated)
                return NotFound(new { Message = "Chat not found" });

            return Ok(new { Success = true, Message = "File content updated" });
        }

        [HttpGet("user")]
        public async Task<IActionResult> GetUserChats(
            [FromQuery] string fileType = null,
            [FromQuery] int? targetLanguageId = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int pageSize = 20,
            [FromQuery] int pageNumber = 1,
            [FromQuery] string sortBy = "LastActivityAt",
            [FromQuery] string sortDirection = "DESC",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in token");
                }

                var filter = new DocumentTranslationChatFilter
                {
                    UserId = userId,
                    FileType = fileType,
                    TargetLanguageId = targetLanguageId,
                    FromDate = fromDate,
                    ToDate = toDate,
                    PageSize = Math.Min(pageSize, 100), 
                    PageNumber = Math.Max(pageNumber, 1),
                    SortBy = sortBy,
                    SortDirection = sortDirection
                };

                var response = await _chatService.GetUserChats(userId, filter, cancellationToken);

                return Ok(new
                {
                    Success = true,
                    Data = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user chats");
                return StatusCode(500, "Internal server error while retrieving chats");
            }
        }

        [HttpDelete("{chatId}")]
        public async Task<IActionResult> DeleteChat(
            string chatId,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in token");
                }

                var success = await _chatService.DeleteChat(chatId, cancellationToken);

                if (!success)
                {
                    return NotFound(new { Message = "Chat not found or could not be deleted" });
                }

                _logger.LogInformation("Chat {ChatId} deleted by user {UserId}", chatId, userId);

                return Ok(new
                {
                    Success = true,
                    Message = "Chat deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
                return StatusCode(500, "Internal server error while deleting chat");
            }
        }

        private string GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst("UserId")?.Value;
        }

        private static string GetFileType(string fileName)
        {
            try
            {
                var extension = System.IO.Path.GetExtension(fileName)?.TrimStart('.').ToLowerInvariant();
                return string.IsNullOrEmpty(extension) ? "unknown" : extension;
            }
            catch
            {
                return "unknown";
            }
        }
    }
} 

