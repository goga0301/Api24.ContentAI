using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Path = System.IO.Path;
using System.Collections.Generic;
using System.Linq;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentTranslationService _documentTranslationService;
        private readonly IPdfService _pdfService;
        private readonly ILogger<DocumentController> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ITranslationJobService _translationJobService;
        private readonly IDocumentSuggestionService _documentSuggestionService;

        public DocumentController(
            IDocumentTranslationService documentTranslationService,
            IPdfService pdfService,
            ILogger<DocumentController> logger,
            IServiceScopeFactory serviceScopeFactory,
            ITranslationJobService translationJobService,
            IDocumentSuggestionService documentSuggestionService)
        {
            _documentTranslationService = documentTranslationService ?? throw new ArgumentNullException(nameof(documentTranslationService));
            _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _translationJobService = translationJobService ?? throw new ArgumentNullException(nameof(translationJobService));
            _documentSuggestionService = documentSuggestionService ?? throw new ArgumentNullException(nameof(documentSuggestionService));
        }
        
        private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100MB limit
        
        [HttpPost("tesseract/translate")]
        public async Task<IActionResult> TranslateDocumentWithTesseract([FromForm] DocumentTranslationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    userId = User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Unauthorized("User ID not found in token");
                    }
                }

                if (request.File == null || request.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (request.File.Length > MaxFileSizeBytes)
                {
                    return BadRequest($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB");
                }

                var fileExtension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                var estimatedMinutes = GetEstimatedProcessingMinutes(fileExtension);
                var jobId = _translationJobService.CreateJob(fileExtension, request.File.Length / 1024, estimatedMinutes);
                
                // Save file to temporary location instead of memory
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var translationService = scope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        
                        // Create FormFile from temp file
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        
                        // Update progress to show processing started
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var result = await translationService.TranslateDocumentWithTesseract(
                            formFile,
                            request.TargetLanguageId,
                            userId,
                            request.OutputFormat,
                            cancellationToken);

                        if (result.Success)
                        {
                            // Generate suggestions after successful translation
                            List<TranslationSuggestion> suggestions = new List<TranslationSuggestion>();
                            try
                            {
                                var suggestionService = scope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken);
                                
                                _logger.LogInformation("Generated {Count} suggestions for tesseract translation job {JobId}", 
                                    suggestions.Count, jobId);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for tesseract translation job {JobId}", jobId);
                                // Continue without suggestions rather than failing the job
                            }

                            // Complete the job with the translation result and suggestions
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            _logger.LogInformation("Tesseract translation job {JobId} completed successfully", jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            _logger.LogWarning("Tesseract translation job {JobId} failed: {ErrorMessage}", jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        await jobService.FailJob(jobId, ex.Message);
                        _logger.LogError(ex, "Tesseract translation job {JobId} failed: {ErrorMessage}", jobId, ex.Message);
                    }
                    finally
                    {
                        // Clean up temp file
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    Message = "Tesseract translation started in background. Use the job ID to check status.",
                    EstimatedTimeMinutes = estimatedMinutes,
                    FileType = fileExtension,
                    FileSizeKB = request.File.Length / 1024
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error translating document: {ex.Message}");
            }
        }

        [HttpPost("translate")]
        public async Task<IActionResult> TranslateDocumentWithClaude([FromForm] DocumentTranslationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    userId = User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Unauthorized("User ID not found in token");
                    }
                }

                if (request.File == null || request.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (request.File.Length > MaxFileSizeBytes)
                {
                    return BadRequest($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB");
                }

                var fileExtension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                var estimatedMinutes = GetEstimatedProcessingMinutes(fileExtension);
                var jobId = _translationJobService.CreateJob(fileExtension, request.File.Length / 1024, estimatedMinutes);
                
                // Save file to temporary location instead of memory
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var translationService = scope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var result = await translationService.TranslateDocumentWithClaude(
                            formFile,
                            request.TargetLanguageId,
                            userId,
                            request.OutputFormat,
                            request.Model,
                            cancellationToken);

                        if (result.Success)
                        {
                            List<TranslationSuggestion> suggestions = new List<TranslationSuggestion>();
                            try
                            {
                                var suggestionService = scope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken);
                                
                                _logger.LogInformation("Generated {Count} suggestions for translation job {JobId}", 
                                    suggestions.Count, jobId);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for translation job {JobId}", jobId);
                                // Continue without suggestions rather than failing the job
                            }

                            // Complete the job with the translation result and suggestions
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            _logger.LogInformation("Translation job {JobId} completed successfully", jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            _logger.LogWarning("Translation job {JobId} failed: {ErrorMessage}", jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        await jobService.FailJob(jobId, ex.Message);
                        _logger.LogError(ex, "Translation job {JobId} failed: {ErrorMessage}", jobId, ex.Message);
                    }
                    finally
                    {
                        // Clean up temp file
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    Message = "Translation started in background. Use the job ID to check status.",
                    EstimatedTimeMinutes = estimatedMinutes,
                    FileType = fileExtension,
                    FileSizeKB = request.File.Length / 1024
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error translating document: {ex.Message}");
            }
        }

        [HttpGet("translate/status/{jobId}")]
        public async Task<IActionResult> GetTranslationStatus(string jobId)
        {
            try
            {
                var job = await _translationJobService.GetJob(jobId);
                if (job == null)
                {
                    return NotFound(new { JobId = jobId, Message = "Job not found" });
                }

                var elapsed = DateTime.UtcNow - job.StartTime;
                var estimatedRemaining = job.Status switch
                {
                    "Completed" => 0,
                    "Failed" => 0,
                    _ => Math.Max(0, job.EstimatedTimeMinutes - (int)elapsed.TotalMinutes)
                };

                return Ok(new
                {
                    JobId = jobId,
                    Status = job.Status,
                    Progress = job.Progress,
                    Message = job.Status switch
                    {
                        "Completed" => "Translation completed successfully",
                        "Failed" => $"Translation failed: {job.ErrorMessage}",
                        _ => "Translation in progress..."
                    },
                    EstimatedRemainingMinutes = estimatedRemaining
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error checking translation status: {ex.Message}");
            }
        }

        [HttpGet("translate/result/{jobId}")]
        public async Task<IActionResult> GetTranslationResult(string jobId)
        {
            try
            {
                var job = await _translationJobService.GetJob(jobId);
                if (job == null)
                {
                    return NotFound(new { JobId = jobId, Message = "Job not found" });
                }

                if (job.Status != "Completed" || job.ResultData == null)
                {
                    return BadRequest(new 
                    { 
                        JobId = jobId, 
                        Status = job.Status,
                        Message = job.Status == "Failed" 
                            ? $"Translation failed: {job.ErrorMessage}" 
                            : "Translation not completed yet" 
                    });
                }

                return File(job.ResultData, job.ContentType ?? "application/octet-stream", job.FileName ?? "translated-file");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error retrieving translation result: {ex.Message}");
            }
        }

        [HttpGet("translate/suggestions/{jobId}")]
        public async Task<IActionResult> GetTranslationSuggestions(string jobId)
        {
            try
            {
                var job = await _translationJobService.GetJob(jobId);
                if (job == null)
                {
                    return NotFound(new { JobId = jobId, Message = "Job not found" });
                }

                if (job.Status != "Completed")
                {
                    return BadRequest(new 
                    { 
                        JobId = jobId, 
                        Status = job.Status,
                        Message = job.Status == "Failed" 
                            ? $"Translation failed: {job.ErrorMessage}" 
                            : "Translation not completed yet" 
                    });
                }

                return Ok(new
                {
                    JobId = jobId,
                    SuggestionCount = job.Suggestions.Count,
                    Suggestions = job.Suggestions,
                    Message = job.Suggestions.Any() 
                        ? $"Found {job.Suggestions.Count} suggestions for improving the translation"
                        : "No suggestions available for this translation"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error retrieving translation suggestions: {ex.Message}");
            }
        }

        [HttpPost("srt/translate")]
        public async Task<IActionResult> TranslateSrtFile([FromForm] DocumentTranslationRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    userId = User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Unauthorized("User ID not found in token");
                    }
                }

                if (request.File == null || request.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (!request.File.FileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Only SRT files are supported for this endpoint");
                }

                if (request.File.Length > MaxFileSizeBytes)
                {
                    return BadRequest($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB");
                }

                var fileExtension = ".srt";
                var estimatedMinutes = GetEstimatedProcessingMinutes(fileExtension);
                var jobId = _translationJobService.CreateJob(fileExtension, request.File.Length / 1024, estimatedMinutes);
                
                // Save file to temporary location instead of memory
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var translationService = scope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        
                        // Create FormFile from temp file
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        
                        // Update progress to show processing started
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var result = await translationService.TranslateSRTFiles(
                            formFile,
                            request.TargetLanguageId,
                            userId,
                            request.OutputFormat,
                            cancellationToken);

                        if (result.Success)
                        {
                            // Generate suggestions after successful translation
                            List<TranslationSuggestion> suggestions = new List<TranslationSuggestion>();
                            try
                            {
                                var suggestionService = scope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken);
                                
                                _logger.LogInformation("Generated {Count} suggestions for SRT translation job {JobId}", 
                                    suggestions.Count, jobId);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for SRT translation job {JobId}", jobId);
                                // Continue without suggestions rather than failing the job
                            }

                            // Complete the job with the translation result and suggestions
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            _logger.LogInformation("SRT translation job {JobId} completed successfully", jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            _logger.LogWarning("SRT translation job {JobId} failed: {ErrorMessage}", jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var jobService = scope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        await jobService.FailJob(jobId, ex.Message);
                        _logger.LogError(ex, "SRT translation job {JobId} failed: {ErrorMessage}", jobId, ex.Message);
                    }
                    finally
                    {
                        // Clean up temp file
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    Message = "SRT translation started in background. Use the job ID to check status.",
                    EstimatedTimeMinutes = estimatedMinutes,
                    FileType = fileExtension,
                    FileSizeKB = request.File.Length / 1024
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error translating SRT file: {ex.Message}");
            }
        }

        [HttpPost("convert/markdown-to-pdf")]
        public async Task<IActionResult> ConvertMarkdownToPdf([FromForm] DocumentConvertRequest convertRequest, CancellationToken cancellation)
        {
            try
            {
                if (convertRequest.File == null || convertRequest.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (!convertRequest.File.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Only Markdown files are supported for this endpoint");
                }
                
                var pdfBytes = await _pdfService.ConvertMarkdownToPdf(convertRequest.File, cancellation);
                var fileName = Path.GetFileNameWithoutExtension(convertRequest.File.FileName) + ".pdf";

                return File(
                    fileContents: pdfBytes,
                    contentType: "application/pdf",
                    fileDownloadName: fileName
                );
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error converting markdown to pdf");
                throw;
            }
            
        }

        [HttpPost("convert/pdf-to-word")]
        public async Task<IActionResult> ConvertPdfToWord([FromForm] DocumentConvertRequest convertRequest, CancellationToken cancellation)
        {
            try
            {
                if (convertRequest.File == null || convertRequest.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (!convertRequest.File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Only PDF files are supported for this endpoint");
                }   

                var wordBytes = await _pdfService.ConvertPdfToWord(convertRequest.File, cancellation);
                var fileName = Path.GetFileNameWithoutExtension(convertRequest.File.FileName) + ".docx";

                return File(
                    fileContents: wordBytes,
                    contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    fileDownloadName: fileName
                );
            }
            catch (Exception e)
            {   
                _logger.LogError(e, "Error converting pdf to word");
                throw;
            }
        }

        [HttpPost("apply-suggestion")]
        public async Task<IActionResult> ApplySuggestion([FromBody] ApplySuggestionRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    userId = User.FindFirst("UserId")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Unauthorized("User ID not found in token");
                    }
                }

                if (request == null)
                {
                    return BadRequest("Request cannot be null");
                }

                if (string.IsNullOrEmpty(request.TranslatedContent))
                {
                    return BadRequest("Translated content is required");
                }

                if (request.Suggestion == null)
                {
                    return BadRequest("Suggestion is required");
                }

                var result = await _documentSuggestionService.ApplySuggestion(request, cancellationToken);

                if (!result.Success)
                {
                    return BadRequest(new { error = result.ErrorMessage });
                }

                _logger.LogInformation("Successfully applied suggestion {SuggestionId} for user {UserId}", 
                    request.SuggestionId, userId);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying suggestion");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error applying suggestion: {ex.Message}");
            }
        }

        private async Task<string> SaveToTempFile(IFormFile file, string jobId, CancellationToken cancellationToken)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ContentAI_Uploads");
            Directory.CreateDirectory(tempDir);

            var tempFileName = $"{jobId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var tempFilePath = Path.Combine(tempDir, tempFileName);
            
            using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(fileStream, cancellationToken);
            
            _logger.LogDebug("Saved file {FileName} to temp location: {TempPath}", file.FileName, tempFilePath);
            return tempFilePath;
        }

        private async Task<IFormFile> CreateFormFileFromTempFile(string tempFilePath, string originalFileName, string? contentType)
        {
            var fileBytes = await System.IO.File.ReadAllBytesAsync(tempFilePath);
            var stream = new MemoryStream(fileBytes);
            
            return new FormFile(stream, 0, fileBytes.Length, "file", originalFileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        private void CleanupTempFile(string tempFilePath)
        {
            try
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                    _logger.LogDebug("Cleaned up temp file: {TempPath}", tempFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp file: {TempPath}", tempFilePath);
            }
        }

        private static int GetEstimatedProcessingMinutes(string fileExtension)
        {
            return 3; 
            // 3 minutes for all files - realistic average estimate
        }

    }
}
