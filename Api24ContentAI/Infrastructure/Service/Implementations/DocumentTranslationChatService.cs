using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationChatService : IDocumentTranslationChatService
    {
        private readonly IDocumentTranslationChatRepository _chatRepository;
        private readonly ILanguageService _languageService;
        private readonly ILogger<DocumentTranslationChatService> _logger;

        public DocumentTranslationChatService(
            IDocumentTranslationChatRepository chatRepository,
            ILanguageService languageService,
            ILogger<DocumentTranslationChatService> logger)
        {
            _chatRepository = chatRepository ?? throw new ArgumentNullException(nameof(chatRepository));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Chat Management

        public async Task<DocumentTranslationChatResponse> StartChat(CreateDocumentTranslationChatModel model, CancellationToken cancellationToken)
        {
            try
            {
                var targetLanguage = await _languageService.GetById(model.TargetLanguageId, cancellationToken);
                if (targetLanguage == null)
                {
                    throw new ArgumentException($"Target language with ID {model.TargetLanguageId} not found");
                }

                var chatId = Guid.NewGuid().ToString();
                var title = await GenerateChatTitle(model.OriginalFileName, targetLanguage.Name);

                var chat = new DocumentTranslationChat
                {
                    ChatId = chatId,
                    UserId = model.UserId,
                    OriginalFileName = model.OriginalFileName,
                    OriginalContentType = model.OriginalContentType,
                    OriginalFileSizeBytes = model.OriginalFileSizeBytes,
                    FileType = model.FileType,
                    TargetLanguageId = model.TargetLanguageId,
                    TargetLanguageName = targetLanguage.Name,
                    Title = title,
                    Status = "Processing" // Status: Processing, Completed, Failed
                };

                await _chatRepository.CreateChat(chat, cancellationToken);

                _logger.LogInformation("Started new document translation chat {ChatId} for user {UserId} with file {FileName}", 
                    chatId, model.UserId, model.OriginalFileName);

                return await GetChat(chatId, cancellationToken) 
                    ?? throw new Exception("Failed to retrieve created chat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting document translation chat for user {UserId}", model.UserId);
                throw;
            }
        }

        public async Task<DocumentTranslationChatResponse?> GetChat(
            string chatId, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var chat = await _chatRepository.GetChatById(chatId, cancellationToken);
                if (chat == null)
                    return null;

                DocumentTranslationResult? translationResult = null;
                if (!string.IsNullOrEmpty(chat.TranslationResult))
                {
                    try
                    {
                        translationResult = JsonSerializer.Deserialize<DocumentTranslationResult>(chat.TranslationResult, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            PropertyNameCaseInsensitive = true
                        });
                        _logger.LogInformation("Deserialized translation result for chat {ChatId}. Success: {Success}, " +
                            "TranslatedContent length: {TranslatedContentLength}, OriginalContent length: {OriginalContentLength}", 
                            chatId, translationResult?.Success ?? false,
                            translationResult?.TranslatedContent?.Length ?? 0,
                            translationResult?.OriginalContent?.Length ?? 0);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize translation result for chat {ChatId}. JSON: {Json}", 
                            chatId, chat.TranslationResult);
                    }
                }
                else
                {
                    _logger.LogInformation("No translation result data found for chat {ChatId}", chatId);
                }

                return new DocumentTranslationChatResponse
                {
                    ChatId = chat.ChatId,
                    Status = chat.Status,
                    Title = chat.Title ?? "Document Translation",
                    OriginalFileName = chat.OriginalFileName,
                    FileType = chat.FileType,
                    TargetLanguageName = chat.TargetLanguageName ?? "Unknown",
                    CreatedAt = chat.CreatedAt,
                    LastActivityAt = chat.LastActivityAt,
                    TranslationResult = translationResult,
                    ErrorMessage = chat.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
                throw;
            }
        }

        public async Task<DocumentTranslationChatListResponse> GetUserChats(string userId, DocumentTranslationChatFilter filter = null, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                filter ??= new DocumentTranslationChatFilter { UserId = userId };
                filter.UserId = userId; 

                var (chats, totalCount) = await _chatRepository.GetChatsFiltered(filter, cancellationToken);

                var chatSummaries = new List<DocumentTranslationChatSummary>();
                foreach (var chat in chats)
                {
                    chatSummaries.Add(new DocumentTranslationChatSummary
                    {
                        ChatId = chat.ChatId,
                        Title = chat.Title ?? "Document Translation",
                        OriginalFileName = chat.OriginalFileName,
                        FileType = chat.FileType,
                        TargetLanguageName = chat.TargetLanguageName ?? "Unknown",
                        Status = chat.Status,
                        CreatedAt = chat.CreatedAt,
                        LastActivityAt = chat.LastActivityAt,
                        HasResult = !string.IsNullOrEmpty(chat.TranslationResult),
                        HasError = !string.IsNullOrEmpty(chat.ErrorMessage)
                    });
                }

                return new DocumentTranslationChatListResponse
                {
                    Chats = chatSummaries,
                    TotalCount = totalCount,
                    PageSize = filter.PageSize,
                    PageNumber = filter.PageNumber
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chats for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> DeleteChat(string chatId, CancellationToken cancellationToken)
        {
            try
            {
                return await _chatRepository.DeleteChat(chatId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
                throw;
            }
        }

        #endregion

        #region Translation Integration

        public async Task<DocumentTranslationChatResponse> AddTranslationResult(string chatId, string userId, DocumentTranslationResult result, string translationJobId, 
            CancellationToken cancellationToken)
        {
            try
            {
                var chat = await _chatRepository.GetChatById(chatId, cancellationToken);
                if (chat == null)
                {
                    throw new ArgumentException($"Chat {chatId} not found");
                }

                _logger.LogInformation("Adding translation result to chat {ChatId}. Success: {Success}, " +
                    "TranslatedContent length: {TranslatedContentLength}, OriginalContent length: {OriginalContentLength}, " +
                    "FileName: {FileName}, ContentType: {ContentType}, TranslationId: {TranslationId}, Cost: {Cost}", 
                    chatId, result.Success, 
                    result.TranslatedContent?.Length ?? 0, 
                    result.OriginalContent?.Length ?? 0,
                    result.FileName ?? "null",
                    result.ContentType ?? "null",
                    result.TranslationId ?? "null",
                    result.Cost);

                chat.Status = result.Success ? "Completed" : "Failed";
                chat.LastActivityAt = DateTime.UtcNow;
                
                if (result.Success)
                {
                    chat.TranslationResult = JsonSerializer.Serialize(result, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    chat.ErrorMessage = null;
                    
                    _logger.LogInformation("Serialized translation result for chat {ChatId}. JSON length: {JsonLength}", 
                        chatId, chat.TranslationResult?.Length ?? 0);
                }
                else
                {
                    chat.ErrorMessage = result.ErrorMessage;
                    chat.TranslationResult = null;
                    _logger.LogWarning("Translation failed for chat {ChatId}. Error: {ErrorMessage}", 
                        chatId, result.ErrorMessage);
                }

                await _chatRepository.UpdateChat(chat, cancellationToken);

                _logger.LogInformation("Updated chat {ChatId} with translation result. Success: {Success}", 
                    chatId, result.Success);

                return await GetChat(chatId, cancellationToken) 
                    ?? throw new Exception("Failed to retrieve updated chat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding translation result to chat {ChatId}", chatId);
                throw;
            }
        }

        public async Task<DocumentTranslationChatResponse> AddErrorMessage(
            string chatId, 
            string userId, 
            string errorMessage, 
            string? translationJobId = null, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var chat = await _chatRepository.GetChatById(chatId, cancellationToken);
                if (chat == null)
                {
                    throw new ArgumentException($"Chat {chatId} not found");
                }

                // Store the error directly in the chat
                chat.Status = "Failed";
                chat.ErrorMessage = errorMessage;
                chat.TranslationResult = null;
                chat.LastActivityAt = DateTime.UtcNow;

                await _chatRepository.UpdateChat(chat, cancellationToken);

                _logger.LogInformation("Updated chat {ChatId} with error: {ErrorMessage}", chatId, errorMessage);

                return await GetChat(chatId, cancellationToken) 
                    ?? throw new Exception("Failed to retrieve updated chat");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding error message to chat {ChatId}", chatId);
                throw;
            }
        }

        #endregion

        #region Utility Methods

        public async Task<string> GenerateChatTitle(string fileName, string targetLanguage)
        {
            try
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                return $"Translate {fileNameWithoutExtension} to {targetLanguage}";
            }
            catch
            {
                return $"Document Translation to {targetLanguage}";
            }
        }

        public async Task CleanupOldChats(int daysOld = 90, CancellationToken cancellationToken = default)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
                await _chatRepository.DeleteChatsOlderThan(cutoffDate, cancellationToken);
                
                _logger.LogInformation("Cleaned up chats older than {DaysOld} days", daysOld);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during chat cleanup");
                throw;
            }
        }

        #endregion
    }
} 
