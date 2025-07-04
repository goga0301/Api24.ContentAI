using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface IDocumentTranslationChatRepository
    {
        Task<Guid> CreateChat(DocumentTranslationChat chat, CancellationToken cancellationToken);
        Task<DocumentTranslationChat?> GetChatById(string chatId, CancellationToken cancellationToken);
        Task<bool> UpdateChat(DocumentTranslationChat chat, CancellationToken cancellationToken);
        Task<bool> DeleteChat(string chatId, CancellationToken cancellationToken);
        
        Task<(List<DocumentTranslationChat> chats, int totalCount)> GetChatsFiltered(
            DocumentTranslationChatFilter filter, 
            CancellationToken cancellationToken);
        
        Task DeleteChatsOlderThan(DateTime cutoffDate, CancellationToken cancellationToken);
    }
} 