using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface ITranslationJobRepository
    {
        Task<string> Create(TranslationJobEntity entity, CancellationToken cancellationToken);
        Task<string> CreateWithModel(string fileType, long fileSizeKB, int estimatedTimeMinutes, string userId, AIModel model, CancellationToken cancellationToken);
        Task<TranslationJobEntity?> GetByJobId(string jobId, CancellationToken cancellationToken);
        Task UpdateProgress(string jobId, int progress, CancellationToken cancellationToken);
        Task CompleteJob(string jobId, byte[] resultData, string fileName, string contentType, List<TranslationSuggestion>? suggestions, CancellationToken cancellationToken);
        Task FailJob(string jobId, string errorMessage, CancellationToken cancellationToken);
        Task CleanupExpiredJobs(CancellationToken cancellationToken);
        Task UpdateReturnedSuggestionIds(string jobId, List<string> returnedSuggestionIds, CancellationToken cancellationToken);
    }
} 