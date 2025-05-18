using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Repository;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentTranslationService _documentTranslationService;
        private readonly IUserRepository _userRepository;

        public DocumentController(IDocumentTranslationService documentTranslationService, IUserRepository userRepository)
        {
            _documentTranslationService = documentTranslationService ?? throw new ArgumentNullException(nameof(documentTranslationService));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        }

        [HttpPost("translate")]
        public async Task<IActionResult> TranslateDocument([FromForm] DocumentTranslationRequest request, CancellationToken cancellationToken)
        {
            try
            {
                string userId = User.FindFirst("sub")?.Value;
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

                var result = await _documentTranslationService.TranslateDocument(
                    request.File,
                    request.TargetLanguageId,
                    userId,
                    request.OutputFormat,
                    cancellationToken);

                if (!result.Success)
                {
                    Console.WriteLine($"Translation failed: {result.ErrorMessage}");
                    return BadRequest(result.ErrorMessage);
                }

                Console.WriteLine("Translation completed successfully");
                
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
                Console.WriteLine($"Error in TranslateDocument: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error translating document: {ex.Message}");
            }
        }
    }
}
