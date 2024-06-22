using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;

        public ContentController(IContentService contentService)
        {
            _contentService = contentService;
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

    }
}