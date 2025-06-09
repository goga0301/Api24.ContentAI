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

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentController(
        IDocumentTranslationService documentTranslationService,
        IPdfService pdfService,
        ILogger<DocumentController> logger,
        IServiceScopeFactory serviceScopeFactory,
        ITranslationJobService translationJobService)
        : ControllerBase
    {
        private readonly IDocumentTranslationService _documentTranslationService = documentTranslationService ?? throw new ArgumentNullException(nameof(documentTranslationService));
        private readonly IPdfService _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
        private readonly ILogger<DocumentController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        private readonly ITranslationJobService _translationJobService = translationJobService ?? throw new ArgumentNullException(nameof(translationJobService));
        
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
                            // Complete the job with the translation result
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain");
                                
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
                        
                        // Create FormFile from temp file
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        
                        // Update progress to show processing started
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var result = await translationService.TranslateDocumentWithClaude(
                            formFile,
                            request.TargetLanguageId,
                            userId,
                            request.OutputFormat,
                            cancellationToken);

                        if (result.Success)
                        {
                            // Complete the job with the translation result
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain");
                                
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
                            // Complete the job with the translation result
                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain");
                                
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
                Console.WriteLine(e);
                throw;
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
            // Generic estimate - actual time varies based on content complexity, not file type
            return 3; // 3 minutes for all files - realistic average estimate
        }
    }
}