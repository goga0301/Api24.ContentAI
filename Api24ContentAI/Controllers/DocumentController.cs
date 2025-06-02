using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using iText.Kernel.Geom;
using Path = System.IO.Path;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentController(
        IDocumentTranslationService documentTranslationService, IPdfService pdfService)
        : ControllerBase
    {
        private readonly IDocumentTranslationService _documentTranslationService = documentTranslationService ?? throw new ArgumentNullException(nameof(documentTranslationService));

        private readonly IPdfService _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
        
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

                var result = await _documentTranslationService.TranslateDocumentWithTesseract(
                    request.File,
                    request.TargetLanguageId,
                    userId,
                    request.OutputFormat,
                    cancellationToken);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }
                
                if (result.FileData != null && result.FileData.Length > 0)
                {
                    return File(result.FileData, result.ContentType, result.FileName);
                }
                else
                {
                    return Ok(new
                    {
                        result.Success,
                        result.TranslatedContent,
                        result.FileName,
                        result.ContentType,
                        result.TranslationQualityScore,
                        result.TranslationId,
                        result.Cost
                    });
                }
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

                var result = await _documentTranslationService.TranslateDocumentWithClaude(
                    request.File,
                    request.TargetLanguageId,
                    userId,
                    request.OutputFormat,
                    cancellationToken);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }
                
                if (result.FileData != null && result.FileData.Length > 0)
                {
                    return File(result.FileData, result.ContentType, result.FileName);
                }
                else
                {
                    return Ok(new
                    {
                        result.Success,
                        result.TranslatedContent,
                        result.FileName,
                        result.ContentType,
                        result.TranslationQualityScore,
                        result.TranslationId,
                        result.Cost
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error translating document: {ex.Message}");
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

                var result = await _documentTranslationService.TranslateSRTFiles(
                    request.File,
                    request.TargetLanguageId,
                    userId,
                    request.OutputFormat,
                    cancellationToken);

                if (!result.Success)
                {
                    return BadRequest(result.ErrorMessage);
                }
        
                if (result.FileData != null && result.FileData.Length > 0)
                {
                    return File(result.FileData, result.ContentType, result.FileName);
                }
                else
                {
                    return Ok(new
                    {
                        result.Success,
                        result.TranslatedContent,
                        result.FileName,
                        result.ContentType,
                        result.TranslationQualityScore,
                        result.TranslationId,
                        result.Cost
                    });
                }
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
    }
}