using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class DocumentTranslationChatRepository : IDocumentTranslationChatRepository
    {
        private readonly ContentDbContext _dbContext;
        private readonly ILogger<DocumentTranslationChatRepository> _logger;

        public DocumentTranslationChatRepository(
            ContentDbContext dbContext,
            ILogger<DocumentTranslationChatRepository> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Core Chat Operations

        public async Task<Guid> CreateChat(DocumentTranslationChat chat, CancellationToken cancellationToken)
        {
            try
            {
                await _dbContext.DocumentTranslationChats.AddAsync(chat, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogDebug("Created chat {ChatId} for user {UserId}", chat.ChatId, chat.UserId);
                return chat.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating chat {ChatId}", chat.ChatId);
                throw;
            }
        }

        public async Task<DocumentTranslationChat?> GetChatById(string chatId, CancellationToken cancellationToken)
        {
            try
            {
                return await _dbContext.DocumentTranslationChats
                    .FirstOrDefaultAsync(x => x.ChatId == chatId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
                throw;
            }
        }

        public async Task<bool> UpdateChat(DocumentTranslationChat chat, CancellationToken cancellationToken)
        {
            try
            {
                var existingChat = await GetChatById(chat.ChatId, cancellationToken);
                if (existingChat == null)
                    return false;

                existingChat.Status = chat.Status;
                existingChat.Title = chat.Title;
                existingChat.LastActivityAt = chat.LastActivityAt;
                existingChat.TranslationResult = chat.TranslationResult;
                existingChat.ErrorMessage = chat.ErrorMessage;
                existingChat.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating chat {ChatId}", chat.ChatId);
                throw;
            }
        }

        public async Task<bool> DeleteChat(string chatId, CancellationToken cancellationToken)
        {
            try
            {
                var chat = await GetChatById(chatId, cancellationToken);
                if (chat == null)
                    return false;

                _dbContext.DocumentTranslationChats.Remove(chat);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogDebug("Deleted chat {ChatId}", chatId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
                throw;
            }
        }

        #endregion

        #region Chat Querying and Filtering

        public async Task<(List<DocumentTranslationChat> chats, int totalCount)> GetChatsFiltered(
            DocumentTranslationChatFilter filter, 
            CancellationToken cancellationToken)
        {
            try
            {
                var query = _dbContext.DocumentTranslationChats.AsQueryable();

                if (!string.IsNullOrEmpty(filter.UserId))
                {
                    query = query.Where(x => x.UserId == filter.UserId);
                }

                if (!string.IsNullOrEmpty(filter.FileType))
                {
                    query = query.Where(x => x.FileType == filter.FileType);
                }

                if (filter.TargetLanguageId.HasValue)
                {
                    query = query.Where(x => x.TargetLanguageId == filter.TargetLanguageId.Value);
                }

                if (filter.FromDate.HasValue)
                {
                    query = query.Where(x => x.CreatedAt >= filter.FromDate.Value);
                }

                if (filter.ToDate.HasValue)
                {
                    query = query.Where(x => x.CreatedAt <= filter.ToDate.Value);
                }

                var totalCount = await query.CountAsync(cancellationToken);

                query = filter.SortBy?.ToLowerInvariant() switch
                {
                    "createdat" => filter.SortDirection?.ToUpperInvariant() == "ASC" 
                        ? query.OrderBy(x => x.CreatedAt)
                        : query.OrderByDescending(x => x.CreatedAt),
                    "title" => filter.SortDirection?.ToUpperInvariant() == "ASC"
                        ? query.OrderBy(x => x.Title)
                        : query.OrderByDescending(x => x.Title),
                    "status" => filter.SortDirection?.ToUpperInvariant() == "ASC"
                        ? query.OrderBy(x => x.Status)
                        : query.OrderByDescending(x => x.Status),
                    _ => filter.SortDirection?.ToUpperInvariant() == "ASC"
                        ? query.OrderBy(x => x.LastActivityAt)
                        : query.OrderByDescending(x => x.LastActivityAt)
                };

                var chats = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync(cancellationToken);

                return (chats, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering chats for user {UserId}", filter.UserId);
                throw;
            }
        }

        #endregion

        #region Maintenance Operations

        public async Task DeleteChatsOlderThan(DateTime cutoffDate, CancellationToken cancellationToken)
        {
            try
            {
                var oldChats = await _dbContext.DocumentTranslationChats
                    .Where(x => x.CreatedAt < cutoffDate)
                    .ToListAsync(cancellationToken);

                if (oldChats.Any())
                {
                    _dbContext.DocumentTranslationChats.RemoveRange(oldChats);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("Deleted {Count} chats older than {CutoffDate}", 
                        oldChats.Count, cutoffDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old chats");
                throw;
            }
        }

        #endregion
    }
} 