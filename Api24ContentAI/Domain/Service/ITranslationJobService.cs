using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface ITranslationJobService
    {
        Task<string> CreateJob(string fileType, long fileSizeKB, int estimatedTimeMinutes, CancellationToken cancellationToken);
        Task<string> CreateJobWithModel(string fileType, long fileSizeKB, int estimatedTimeMinutes, string userId, AIModel model, CancellationToken cancellationToken);
        Task UpdateProgress(string jobId, int progress);
        Task CompleteJob(string jobId, byte[] resultData, string fileName, string contentType, List<TranslationSuggestion>? suggestions = null);
        Task FailJob(string jobId, string errorMessage);
        Task<TranslationJob?> GetJob(string jobId);
        Task CleanupOldJobs();
        Task UpdateReturnedSuggestionIds(string jobId, List<string> returnedSuggestionIds);
        Task<List<TranslationSuggestion>> GetUnreturnedSuggestions(string jobId);
        Task AttachSuggestions(string jobId, List<TranslationSuggestion> suggestions);
    }
} 
