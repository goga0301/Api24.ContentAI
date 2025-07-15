using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class TranslationJobService : ITranslationJobService
    {
        private readonly ILogger<TranslationJobService> _logger;
        private readonly ITranslationJobRepository _repository;

        public TranslationJobService(ILogger<TranslationJobService> logger, ITranslationJobRepository repository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public string CreateJob(string fileType, long fileSizeKB, int estimatedTimeMinutes)
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new TranslationJobEntity
            {
                JobId = jobId,
                FileType = fileType,
                FileSizeKB = fileSizeKB,
                EstimatedTimeMinutes = estimatedTimeMinutes,
                Status = "Processing",
                Progress = 0,
                UserId = "",
                ExpiresAt = DateTime.UtcNow.AddHours(2)
            };

            var result = Task.Run(async () => await _repository.Create(job, CancellationToken.None)).Result;
            
            _logger.LogInformation("Created translation job {JobId} for {FileType} file ({SizeKB}KB)", 
                result, fileType, fileSizeKB);

            return result;
        }

        public async Task<string> CreateJobWithModel(string fileType, long fileSizeKB, int estimatedTimeMinutes, string userId, AIModel model)
        {
            var jobId = await _repository.CreateWithModel(fileType, fileSizeKB, estimatedTimeMinutes, userId, model, CancellationToken.None);
            
            _logger.LogInformation("Created translation job {JobId} for {FileType} file ({SizeKB}KB) with model {Model}", 
                jobId, fileType, fileSizeKB, model);

            return jobId;
        }

        public async Task UpdateProgress(string jobId, int progress)
        {
            await _repository.UpdateProgress(jobId, progress, CancellationToken.None);
        }

        public async Task CompleteJob(string jobId, byte[] resultData, string fileName, string contentType, List<TranslationSuggestion>? suggestions = null)
        {
            await _repository.CompleteJob(jobId, resultData, fileName, contentType, suggestions, CancellationToken.None);
        }

        public async Task FailJob(string jobId, string errorMessage)
        {
            await _repository.FailJob(jobId, errorMessage, CancellationToken.None);
        }

        public async Task UpdateReturnedSuggestionIds(string jobId, List<string> returnedSuggestionIds)
        {
            await _repository.UpdateReturnedSuggestionIds(jobId, returnedSuggestionIds, CancellationToken.None);
        }

        public async Task<TranslationJob?> GetJob(string jobId)
        {
            var jobEntity = await _repository.GetByJobId(jobId, CancellationToken.None);
            if (jobEntity == null) return null;

            var suggestions = new List<TranslationSuggestion>();
            if (!string.IsNullOrEmpty(jobEntity.Suggestions))
            {
                try
                {
                    suggestions = JsonSerializer.Deserialize<List<TranslationSuggestion>>(jobEntity.Suggestions, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }) ?? new List<TranslationSuggestion>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to deserialize suggestions for job {JobId}: {Error}", jobId, ex.Message);
                }
            }

            // Map AI model from entity
            AIModel? usedAIModel = null;
            if (jobEntity.AIModel.HasValue)
            {
                if (Enum.IsDefined(typeof(AIModel), jobEntity.AIModel.Value))
                {
                    usedAIModel = (AIModel)jobEntity.AIModel.Value;
                }
            }

            return new TranslationJob
            {
                JobId = jobEntity.JobId,
                Status = jobEntity.Status,
                Progress = jobEntity.Progress,
                StartTime = jobEntity.StartTime,
                ResultData = jobEntity.ResultData,
                FileName = jobEntity.FileName,
                ContentType = jobEntity.ContentType,
                ErrorMessage = jobEntity.ErrorMessage,
                EstimatedTimeMinutes = jobEntity.EstimatedTimeMinutes,
                FileType = jobEntity.FileType,
                FileSizeKB = jobEntity.FileSizeKB,
                Suggestions = suggestions,
                UsedAIModel = usedAIModel
            };
        }

        public async Task<List<TranslationSuggestion>> GetUnreturnedSuggestions(string jobId)
        {
            var jobEntity = await _repository.GetByJobId(jobId, CancellationToken.None);
            if (jobEntity == null) return new List<TranslationSuggestion>();

            var allSuggestions = new List<TranslationSuggestion>();
            if (!string.IsNullOrEmpty(jobEntity.Suggestions))
            {
                try
                {
                    allSuggestions = JsonSerializer.Deserialize<List<TranslationSuggestion>>(jobEntity.Suggestions, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }) ?? new List<TranslationSuggestion>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to deserialize suggestions for job {JobId}: {Error}", jobId, ex.Message);
                    return new List<TranslationSuggestion>();
                }
            }

            var returnedIds = new List<string>();
            if (!string.IsNullOrEmpty(jobEntity.ReturnedSuggestionIds))
            {
                try
                {
                    returnedIds = JsonSerializer.Deserialize<List<string>>(jobEntity.ReturnedSuggestionIds, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }) ?? new List<string>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to deserialize returned suggestion IDs for job {JobId}: {Error}", jobId, ex.Message);
                }
            }

            var unreturnedSuggestions = allSuggestions.Where(s => !returnedIds.Contains(s.Id)).ToList();
            
            _logger.LogDebug("Job {JobId}: Found {TotalSuggestions} total suggestions, {ReturnedCount} already returned, {UnreturnedCount} unreturned", 
                jobId, allSuggestions.Count, returnedIds.Count, unreturnedSuggestions.Count);

            return unreturnedSuggestions;
        }

        public async Task CleanupOldJobs()
        {
            await _repository.CleanupExpiredJobs(CancellationToken.None);
        }
    }
} 