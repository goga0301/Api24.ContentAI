using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserContentController : ControllerBase
    {
        private readonly IUserContentService _userContentService;
        public UserContentController(IUserContentService userContentService)
        {
            _userContentService = userContentService;
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] UserContentAIRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.SendRequest(request, userId, cancellationToken));
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromBody] UserTranslateRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.Translate(request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("copyright")]
        public async Task<IActionResult> Copyright([FromForm] UserCopyrightAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.CopyrightAI(file, request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("video-script")]
        public async Task<IActionResult> VideoScript([FromForm] UserVideoScriptAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.VideoScript(file, request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

    }
}
