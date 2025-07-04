using Api24ContentAI.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IDocumentTranslationChatService
    {
        Task<DocumentTranslationChatResponse> StartChat(CreateDocumentTranslationChatModel model, CancellationToken cancellationToken);
        
        Task<DocumentTranslationChatResponse> GetChat(string chatId, CancellationToken cancellationToken = default);
        
        Task<DocumentTranslationChatListResponse> GetUserChats(string userId, DocumentTranslationChatFilter filter = null, CancellationToken cancellationToken = default);
        
        Task<bool> DeleteChat(string chatId, CancellationToken cancellationToken);
        
        Task<DocumentTranslationChatResponse> AddTranslationResult(
            string chatId, 
            string userId, 
            DocumentTranslationResult result, 
            string translationJobId, 
            CancellationToken cancellationToken);
        
        Task<DocumentTranslationChatResponse> AddErrorMessage(
            string chatId, 
            string userId, 
            string errorMessage, 
            string? translationJobId = null, 
            CancellationToken cancellationToken = default);
        
        Task<string> GenerateChatTitle(string fileName, string targetLanguage);
        Task CleanupOldChats(int daysOld = 90, CancellationToken cancellationToken = default);
    }
} 
