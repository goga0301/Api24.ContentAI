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

        public async Task<string> CreateWithModel(string fileType, long fileSizeKB, int estimatedTimeMinutes, string userId, AIModel model, CancellationToken cancellationToken)
        {
            var jobId = Guid.NewGuid().ToString();
            var entity = new TranslationJobEntity
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FileType = fileType,
                FileSizeKB = fileSizeKB,
                EstimatedTimeMinutes = estimatedTimeMinutes,
                Status = "Processing",
                Progress = 0,
                UserId = userId,
                AIModel = (int)model,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(2)
            };
            
            await _dbContext.TranslationJobs.AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Created translation job {JobId} for user {UserId} with model {Model}", jobId, userId, model);
            return jobId;
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

        public async Task UpdateReturnedSuggestionIds(string jobId, List<string> returnedSuggestionIds, CancellationToken cancellationToken)
        {
            var job = await _dbContext.TranslationJobs
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
                
            if (job != null)
            {
                var existingIds = new List<string>();
                if (!string.IsNullOrEmpty(job.ReturnedSuggestionIds))
                {
                    try
                    {
                        existingIds = JsonSerializer.Deserialize<List<string>>(job.ReturnedSuggestionIds) ?? new List<string>();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to deserialize existing returned suggestion IDs for job {JobId}: {Error}", jobId, ex.Message);
                    }
                }

                // Add new IDs to existing ones (avoid duplicates)
                foreach (var id in returnedSuggestionIds)
                {
                    if (!existingIds.Contains(id))
                    {
                        existingIds.Add(id);
                    }
                }

                // Serialize back to JSON
                job.ReturnedSuggestionIds = JsonSerializer.Serialize(existingIds, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                job.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogDebug("Updated returned suggestion IDs for job {JobId}. Total returned: {Count}", jobId, existingIds.Count);
            }
        }

        public async Task FailJob(string jobId, string errorMessage, CancellationToken cancellationToken)
        {
            var job = await _dbContext.TranslationJobs
                .FirstOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
                
            if (job != null)
            {
                job.Status = "Failed";
                job.ErrorMessage = errorMessage;
                job.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                _logger.LogInformation("Translation job {JobId} failed: {ErrorMessage}", jobId, errorMessage);
            }
        }

        public async Task CleanupExpiredJobs(CancellationToken cancellationToken)
        {
            var expiredJobs = await _dbContext.TranslationJobs
                .Where(x => x.ExpiresAt < DateTime.UtcNow)
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