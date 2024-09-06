using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly HttpClient _httpClient;

        public ContentController(IContentService contentService, HttpClient httpClient)
        {
            _contentService = contentService;
            _httpClient = httpClient;

        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] ContentAIRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _contentService.SendRequest(request, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromBody] TranslateRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _contentService.Translate(request, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("copyright")]
        public async Task<IActionResult> Copyright([FromForm] CopyrightAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _contentService.CopyrightAI(file, request, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("video-script")]
        public async Task<IActionResult> VideoScript([FromForm] VideoScriptAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _contentService.VideoScript(file, request, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }


        [HttpPost("prompt")]
        public async Task<IActionResult> Prompt([FromBody] VoyagePromptRequest prompt, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<PromptResponse>($"http://localhost:8000/rag/?prompt={prompt.Prompt}&k=5&model=claude-3-sonnet-20240229", cancellationToken);
                return Ok(response);

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }


        [HttpPost("lawyer")]
        public async Task<IActionResult> Lawyer([FromBody] LawyerRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _contentService.Lawyer(request, cancellationToken));
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

    }

    public class PromptResponse
    {
        public string Query { get; set; }
        public string Response { get; set; }
    }
}