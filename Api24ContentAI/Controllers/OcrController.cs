using System;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Api24ContentAI.Controllers
{


    [ApiController]
    [Route("api/[controller]")]
    public class OcrController(IClaudeService claudeService, ICacheService cacheService) : ControllerBase
    {
        private readonly IClaudeService _claudeService = claudeService;
        private readonly ICacheService _cacheService = cacheService;

        [HttpPost("extract")]
        public async Task<IActionResult> ExtractText(IFormFile file, string language, CancellationToken cancellationToken)
        {
            try
            {
                // Generate a unique key for this OCR request
                string cacheKey = $"ocr_{Guid.NewGuid()}";

                string ocrText = await ProcessAndCacheOcrResult(file, language, cacheKey, cancellationToken);

                return Ok(new { text = ocrText, cacheKey });
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }


        [HttpPost("process")]
        public async Task<IActionResult> ProcessOcrResult(string cacheKey, string prompt, CancellationToken cancellationToken)
        {
            try
            {
                string ocrText = await _cacheService.GetAsync<string>(cacheKey, cancellationToken);
                if (string.IsNullOrEmpty(ocrText))
                {
                    return BadRequest(new Error { ErrorText = "OCR result not found or expired" });
                }

                // Combine OCR text with prompt for the second LLM
                string combinedPrompt = $"{prompt}\n\nExtracted text from image:\n{ocrText}";

                ClaudeRequest claudeRequest = new(combinedPrompt);
                ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

                return Ok(new ContentAIResponse
                {
                    Text = claudeResponse.Content.Single().Text.Replace("\n", "<br>")
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }


        private async Task<string> ProcessAndCacheOcrResult(IFormFile file, string language, string cacheKey, CancellationToken cancellationToken)
        {
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                string extension = file.FileName.Split('.').Last().ToLower();
                string[] supportedExtensions = { "jpeg", "png", "gif", "webp" };

                if (!Array.Exists(supportedExtensions, ext => ext == extension))
                {
                    throw new Exception("File must be in one of these formats: jpeg, png, gif, webp!");
                }

                ContentFile fileMessage = new()
                {
                    Type = "image",
                    Source = new Source()
                    {
                        Type = "base64",
                        MediaType = $"image/{extension}",
                        Data = Convert.ToBase64String(await GetFileBytes(file))
                    }
                };

                ContentFile message = new()
                {
                    Type = "text",
                    Text = $"Extract all text from this image. The text is in {language}. Return only the extracted text."
                };

                ClaudeRequestWithFile claudeRequest = new([fileMessage, message]);
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                return claudeResponse.Content.Single().Text;
            }, TimeSpan.FromHours(2), cancellationToken);
        }

        private static async Task<byte[]> GetFileBytes(IFormFile file)
        {
            using var memoryStream = new System.IO.MemoryStream();
            await file.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }


    }

}
