using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class TranslationJobRepository : ITranslationJobRepository
    {
        private readonly ContentDbContext _dbContext;
        private readonly ILogger<TranslationJobRepository> _logger;

        public TranslationJobRepository(ContentDbContext dbContext, ILogger<TranslationJobRepository> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> Create(TranslationJobEntity entity, CancellationToken cancellationToken)
        {
            entity.Id = Guid.NewGuid();
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            
            await _dbContext.TranslationJobs.AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Created translation job {JobId} for user {UserId}", entity.JobId, entity.UserId);
            return entity.JobId;
        }

        public async Task<TranslationJobEntity?> GetByJobId(string jobId, CancellationToken cancellationToken)
        {
            return await _dbContext.TranslationJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
        }

        public async Task UpdateProgress(string jobId, int progress, CancellationToken cancellationToken)
        {
            var job = await _dbContext.TranslationJobs
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
                
            if (job != null)
            {
                job.Progress = Math.Min(100, Math.Max(0, progress));
                job.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogDebug("Updated job {JobId} progress to {Progress}%", jobId, progress);
            }
        }

        public async Task CompleteJob(string jobId, byte[] resultData, string fileName, string contentType, List<TranslationSuggestion>? suggestions, CancellationToken cancellationToken)
        {
            var job = await _dbContext.TranslationJobs
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
                
            if (job != null)
            {
                job.Status = "Completed";
                job.Progress = 100;
                job.ResultData = resultData;
                job.FileName = fileName;
                job.ContentType = contentType;
                job.CompletedAt = DateTime.UtcNow;
                job.UpdatedAt = DateTime.UtcNow;
                
                // Serialize suggestions to JSON
                if (suggestions != null && suggestions.Any())
                {
                    job.Suggestions = JsonSerializer.Serialize(suggestions, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                }
                
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogInformation("Translation job {JobId} completed successfully with {SuggestionCount} suggestions", 
                    jobId, suggestions?.Count ?? 0);
            }
        }

        public async Task FailJob(string jobId, string errorMessage, CancellationToken cancellationToken)
        {
            var job = await _dbContext.TranslationJobs
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
                
            if (job != null)
            {
                job.Status = "Failed";
                job.ErrorMessage = errorMessage?.Length > 500 
                    ? errorMessage.Substring(0, 497) + "..." 
                    : errorMessage;
                job.UpdatedAt = DateTime.UtcNow;
                
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogWarning("Translation job {JobId} failed: {Error}", jobId, errorMessage);
            }
        }

        public async Task CleanupExpiredJobs(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow;
            var expiredJobs = await _dbContext.TranslationJobs
                .Where(x => x.ExpiresAt < cutoff)
                .ToListAsync(cancellationToken);

            if (expiredJobs.Any())
            {
                _dbContext.TranslationJobs.RemoveRange(expiredJobs);
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogInformation("Cleaned up {Count} expired translation jobs", expiredJobs.Count);
            }
        }
    }
} 