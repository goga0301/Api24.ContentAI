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
        private readonly IDocumentTranslationChatService _chatService;
        private readonly IUserNameExtractionService _userNameExtractionService;
        private readonly ILanguageService _languageService;

        public DocumentController(
            IDocumentTranslationService documentTranslationService,
            IPdfService pdfService,
            ILogger<DocumentController> logger,
            IServiceScopeFactory serviceScopeFactory,
            ITranslationJobService translationJobService,
            IDocumentSuggestionService documentSuggestionService,
            IDocumentTranslationChatService chatService,
            IUserNameExtractionService userNameExtractionService,
            ILanguageService languageService)
        {
            _documentTranslationService = documentTranslationService ?? throw new ArgumentNullException(nameof(documentTranslationService));
            _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _translationJobService = translationJobService ?? throw new ArgumentNullException(nameof(translationJobService));
            _documentSuggestionService = documentSuggestionService ?? throw new ArgumentNullException(nameof(documentSuggestionService));
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _userNameExtractionService = userNameExtractionService ?? throw new ArgumentNullException(nameof(userNameExtractionService));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
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
                var jobId = await _translationJobService.CreateJobWithModel(fileExtension, request.File.Length / 1024, estimatedMinutes, userId, request.Model);
                
                var chatModel = new CreateDocumentTranslationChatModel
                {
                    UserId = userId,
                    OriginalFileName = request.File.FileName,
                    OriginalContentType = request.File.ContentType,
                    OriginalFileSizeBytes = request.File.Length,
                    FileType = fileExtension.TrimStart('.'),
                    TargetLanguageId = request.TargetLanguageId,
                    InitialMessage = "Starting Tesseract OCR translation..."
                };
                
                var chatResponse = await _chatService.StartChat(chatModel, cancellationToken);
                
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    using var taskScope = _serviceScopeFactory.CreateScope();
                    using var activity = _logger.BeginScope(new Dictionary<string, object> { { "job.id", jobId } });
                    _logger.LogInformation("Starting Tesseract translation job {JobId} for user {UserId}", jobId, userId);
                    
                    try
                    {
                        var jobService = taskScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var docService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var chatService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        var result = await docService.TranslateDocumentWithTesseract(
                            formFile, 
                            request.TargetLanguageId, 
                            userId, 
                            request.OutputFormat, 
                            cancellationToken);
                        
                        if (result.Success)
                        {
                            List<TranslationSuggestion> suggestions = new List<TranslationSuggestion>();
                            try
                            {
                                var suggestionService = taskScope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken,
                                    null,
                                    request.Model);
                                
                                _logger.LogInformation("Generated {Count} suggestions for tesseract translation job {JobId} using {Model}", 
                                    suggestions.Count, jobId, request.Model);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for tesseract translation job {JobId}", jobId);
                            }

                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            await chatService.AddTranslationResult(chatResponse.ChatId, userId, result, jobId, cancellationToken);
                                
                            _logger.LogInformation("Tesseract translation job {JobId} completed successfully", jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            await chatService.AddErrorMessage(chatResponse.ChatId, userId, result.ErrorMessage ?? "Translation failed", jobId, cancellationToken);
                            _logger.LogWarning("Tesseract translation job {JobId} failed: {ErrorMessage}", jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var errorScope = _serviceScopeFactory.CreateScope();
                        var jobService = errorScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var chatService = errorScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        await jobService.FailJob(jobId, ex.Message);
                        await chatService.AddErrorMessage(chatResponse.ChatId, userId, $"Translation failed with error: {ex.Message}", jobId, cancellationToken);
                        _logger.LogError(ex, "Tesseract translation job {JobId} failed: {ErrorMessage}", jobId, ex.Message);
                    }
                    finally
                    {
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    ChatId = chatResponse.ChatId,
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
                var jobId = await _translationJobService.CreateJobWithModel(fileExtension, request.File.Length / 1024, estimatedMinutes, userId, request.Model);
                
                var chatModel = new CreateDocumentTranslationChatModel
                {
                    UserId = userId,
                    OriginalFileName = request.File.FileName,
                    OriginalContentType = request.File.ContentType,
                    OriginalFileSizeBytes = request.File.Length,
                    FileType = fileExtension.TrimStart('.'),
                    TargetLanguageId = request.TargetLanguageId,
                    InitialMessage = $"Starting {request.Model} AI translation..."
                };
                
                var chatResponse = await _chatService.StartChat(chatModel, cancellationToken);
                
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    using var taskScope = _serviceScopeFactory.CreateScope();
                    using var activity = _logger.BeginScope(new Dictionary<string, object> { { "job.id", jobId } });
                    _logger.LogInformation("Starting {Model} translation job {JobId} for user {UserId}", request.Model, jobId, userId);
                    
                    try
                    {
                        var jobService = taskScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var docService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var chatService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        var result = await docService.TranslateDocumentWithClaude(
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
                                var suggestionService = taskScope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken,
                                    null,
                                    request.Model);
                                
                                _logger.LogInformation("Generated {Count} suggestions for {Model} translation job {JobId}", 
                                    suggestions.Count, request.Model, jobId);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for {Model} translation job {JobId}", request.Model, jobId);
                            }

                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            await chatService.AddTranslationResult(chatResponse.ChatId, userId, result, jobId, cancellationToken);
                                
                            _logger.LogInformation("{Model} translation job {JobId} completed successfully", request.Model, jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            await chatService.AddErrorMessage(chatResponse.ChatId, userId, result.ErrorMessage ?? "Translation failed", jobId, cancellationToken);
                            _logger.LogWarning("{Model} translation job {JobId} failed: {ErrorMessage}", request.Model, jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var errorScope = _serviceScopeFactory.CreateScope();
                        var jobService = errorScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var chatService = errorScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        await jobService.FailJob(jobId, ex.Message);
                        await chatService.AddErrorMessage(chatResponse.ChatId, userId, $"Translation failed with error: {ex.Message}", jobId, cancellationToken);
                        _logger.LogError(ex, "{Model} translation job {JobId} failed: {ErrorMessage}", request.Model, jobId, ex.Message);
                    }
                    finally
                    {
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    ChatId = chatResponse.ChatId,
                    Message = $"{request.Model} translation started in background. Use the job ID to check status.",
                    EstimatedTimeMinutes = estimatedMinutes,
                    FileType = fileExtension,
                    FileSizeKB = request.File.Length / 1024,
                    Model = request.Model.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting Claude translation");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error starting translation: {ex.Message}");
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

                var allUnreturnedSuggestions = await _translationJobService.GetUnreturnedSuggestions(jobId);
                
                // Limit to 5 suggestions per call
                const int maxSuggestionsPerCall = 5;
                var suggestionsToReturn = allUnreturnedSuggestions.Take(maxSuggestionsPerCall).ToList();
                var remainingCount = allUnreturnedSuggestions.Count - suggestionsToReturn.Count;

                if (suggestionsToReturn.Any())
                {
                    var returnedIds = suggestionsToReturn.Select(s => s.Id).ToList();
                    await _translationJobService.UpdateReturnedSuggestionIds(jobId, returnedIds);

                    _logger.LogInformation("Returned {Count} of {Total} suggestions for job {JobId} using model {Model}. {Remaining} remaining.", 
                        suggestionsToReturn.Count, allUnreturnedSuggestions.Count, jobId, job.UsedAIModel?.ToString() ?? "Unknown", remainingCount);
                }

                return Ok(new
                {
                    JobId = jobId,
                    SuggestionCount = suggestionsToReturn.Count,
                    Suggestions = suggestionsToReturn,
                    HasMoreSuggestions = remainingCount > 0,
                    RemainingCount = remainingCount,
                    UsedAIModel = job.UsedAIModel?.ToString(),
                    Message = suggestionsToReturn.Any() 
                        ? $"Found {suggestionsToReturn.Count} new suggestions for improving the translation" + 
                          (remainingCount > 0 ? $". {remainingCount} more suggestions available - call again to get them." : "")
                        : "No new suggestions available for this translation"
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
                    return BadRequest("No SRT file uploaded");
                }

                var fileExtension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
                if (fileExtension != ".srt")
                {
                    return BadRequest("Only SRT files are supported for this endpoint");
                }

                if (request.File.Length > MaxFileSizeBytes)
                {
                    return BadRequest($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB");
                }

                var estimatedMinutes = GetEstimatedProcessingMinutes(fileExtension);
                var jobId = await _translationJobService.CreateJobWithModel(fileExtension, request.File.Length / 1024, estimatedMinutes, userId, AIModel.Claude4Sonnet);
                
                var targetLanguageData = await _languageService.GetById(request.TargetLanguageId, cancellationToken);
                var chatModel = new CreateDocumentTranslationChatModel
                {
                    UserId = userId,
                    OriginalFileName = request.File.FileName,
                    OriginalContentType = request.File.ContentType,
                    OriginalFileSizeBytes = request.File.Length,
                    FileType = "srt",
                    TargetLanguageId = request.TargetLanguageId,
                    TargetLanguageName = targetLanguageData?.Name,
                    InitialMessage = "Starting SRT subtitle translation..."
                };
                
                var chatResponse = await _chatService.StartChat(chatModel, cancellationToken);
                var chatId = chatResponse.ChatId;
                
                var tempFilePath = await SaveToTempFile(request.File, jobId, cancellationToken);
                
                _ = Task.Run(async () =>
                {
                    using var taskScope = _serviceScopeFactory.CreateScope();
                    using var activity = _logger.BeginScope(new Dictionary<string, object> { { "job.id", jobId } });
                    _logger.LogInformation("Starting SRT translation job {JobId} for user {UserId}", jobId, userId);
                    
                    try
                    {
                        var jobService = taskScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var docService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationService>();
                        var chatService = taskScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        
                        await jobService.UpdateProgress(jobId, 10);
                        
                        var formFile = await CreateFormFileFromTempFile(tempFilePath, request.File.FileName, request.File.ContentType);
                        var result = await docService.TranslateSRTFiles(
                            formFile,
                            request.TargetLanguageId,
                            userId,
                            request.OutputFormat,
                            cancellationToken);
                        
                        if (result.Success)
                        {
                            List<TranslationSuggestion> suggestions = new List<TranslationSuggestion>();
                            try
                            {
                                var suggestionService = taskScope.ServiceProvider.GetRequiredService<IDocumentSuggestionService>();
                                suggestions = await suggestionService.GenerateSuggestions(
                                    result.OriginalContent ?? "",
                                    result.TranslatedContent ?? "",
                                    request.TargetLanguageId,
                                    cancellationToken,
                                    null,
                                    AIModel.Claude4Sonnet);
                                
                                _logger.LogInformation("Generated {Count} suggestions for SRT translation job {JobId}", 
                                    suggestions.Count, jobId);
                            }
                            catch (Exception suggestionEx)
                            {
                                _logger.LogWarning(suggestionEx, "Failed to generate suggestions for SRT translation job {JobId}", jobId);
                            }

                            await jobService.CompleteJob(
                                jobId, 
                                result.FileData ?? System.Text.Encoding.UTF8.GetBytes(result.TranslatedContent ?? ""), 
                                result.FileName ?? "translated-file",
                                result.ContentType ?? "text/plain",
                                suggestions);
                                
                            await chatService.AddTranslationResult(chatId, userId, result, jobId, cancellationToken);
                                
                            _logger.LogInformation("SRT translation job {JobId} completed successfully", jobId);
                        }
                        else
                        {
                            await jobService.FailJob(jobId, result.ErrorMessage ?? "Translation failed");
                            await chatService.AddErrorMessage(chatId, userId, result.ErrorMessage ?? "Translation failed", jobId, cancellationToken);
                            _logger.LogWarning("SRT translation job {JobId} failed: {ErrorMessage}", jobId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        using var errorScope = _serviceScopeFactory.CreateScope();
                        var jobService = errorScope.ServiceProvider.GetRequiredService<ITranslationJobService>();
                        var chatService = errorScope.ServiceProvider.GetRequiredService<IDocumentTranslationChatService>();
                        await jobService.FailJob(jobId, ex.Message);
                        await chatService.AddErrorMessage(chatId, userId, $"Translation failed with error: {ex.Message}", jobId, cancellationToken);
                        _logger.LogError(ex, "SRT translation job {JobId} failed: {ErrorMessage}", jobId, ex.Message);
                    }
                    finally
                    {
                        CleanupTempFile(tempFilePath);
                    }
                }, cancellationToken);

                return Accepted(new
                {
                    JobId = jobId,
                    ChatId = chatId,
                    Message = "SRT translation started in background. Use the job ID to check status.",
                    EstimatedTimeMinutes = estimatedMinutes,
                    FileType = "srt",
                    FileSizeKB = request.File.Length / 1024
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting SRT translation");
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error starting SRT translation: {ex.Message}");
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

        [HttpPost("convert/word-to-pdf")]
        public async Task<IActionResult> ConvertWordToPdf([FromForm] DocumentConvertRequest convertRequest, CancellationToken cancellation)
        {
            try
            {
                if (convertRequest.File == null || convertRequest.File.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (!convertRequest.File.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Only Word files are supported for this endpoint");
                }   

                var pdfBytes = await _pdfService.ConvertWordToPdf(convertRequest.File, cancellation);
                var fileName = Path.GetFileNameWithoutExtension(convertRequest.File.FileName) + ".pdf";

                return File(
                        fileContents: pdfBytes,
                        contentType: "application/pdf",
                        fileDownloadName: fileName
                        );
            }
            catch (Exception e)
            {   
                _logger.LogError(e, "Error converting word to pdf");
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

        [HttpPost("get-user-names")]
        public async Task<IActionResult> GetUserNamesFromInputFile([FromForm] DocumentConvertRequest convertRequest, [FromForm] string language = "English", CancellationToken cancellation = default)
        {
            try
            {
                if (convertRequest?.File == null || convertRequest.File.Length == 0)
                {
                    return BadRequest(new { Success = false, Message = "No file uploaded" });
                }

                var file = convertRequest.File;
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!_userNameExtractionService.IsSupportedFileType(fileExtension))
                {
                    return BadRequest(new 
                    { 
                        Success = false, 
                        Message = $"Unsupported file type: {fileExtension}. Supported types: PDF, Word, Text, Markdown, and Images (PNG, JPG, JPEG)." 
                    });
                }

                if (file.Length > MaxFileSizeBytes)
                {
                    return BadRequest(new 
                    { 
                        Success = false, 
                        Message = $"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB" 
                    });
                }

                _logger.LogInformation("Starting user name extraction from {FileType} file: {FileName} ({FileSize} bytes) for {Language} language", 
                    fileExtension, file.FileName, file.Length, language);

                var result = await _userNameExtractionService.ExtractUserNamesFromFileAsync(file, language, cancellation);

                if (!result.Success)
                {
                    _logger.LogWarning("User name extraction failed for {FileName}: {ErrorMessage}", 
                        file.FileName, result.ErrorMessage);
                    
                    return BadRequest(new 
                    { 
                        Success = false, 
                        Message = result.ErrorMessage,
                        FileType = result.FileType,
                        FileName = result.FileName
                    });
                }

                _logger.LogInformation("Successfully extracted {UserNameCount} user names from {FileName}", 
                    result.UserNames.Count, file.FileName);

                return Ok(new
                {
                    Success = true,
                    Message = $"Successfully extracted {result.UserNames.Count} user names from the file using {language} language context",
                    UserNames = result.UserNames,
                    FileType = result.FileType,
                    FileName = result.FileName,
                    Language = language,
                    TextLength = result.ExtractedTextLength,
                    ExtractionMethod = result.ExtractionMethod
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting user names from file: {FileName}", 
                    convertRequest?.File?.FileName ?? "unknown");
                
                return StatusCode(StatusCodes.Status500InternalServerError, new 
                { 
                    Success = false, 
                    Message = $"Internal server error: {ex.Message}",
                    FileType = convertRequest?.File != null ? Path.GetExtension(convertRequest.File.FileName).ToLowerInvariant() : "unknown",
                    FileName = convertRequest?.File?.FileName ?? "unknown"
                });
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
