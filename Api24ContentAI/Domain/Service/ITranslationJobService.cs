using Api24ContentAI.Domain.Models;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface ITranslationJobService
    {
        string CreateJob(string fileType, long fileSizeKB, int estimatedTimeMinutes);
        Task UpdateProgress(string jobId, int progress);
        Task CompleteJob(string jobId, byte[] resultData, string fileName, string contentType);
        Task FailJob(string jobId, string errorMessage);
        Task<TranslationJob?> GetJob(string jobId);
        Task CleanupOldJobs();
    }
} 