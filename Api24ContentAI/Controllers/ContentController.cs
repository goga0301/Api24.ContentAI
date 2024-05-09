using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly IClaudeService _claudeService;
        private readonly IContentService _contentService;

        public ContentController(IClaudeService claudeService, IContentService contentService)
        {
            _claudeService = claudeService;
            _contentService = contentService;
        }

        [HttpPost("test")]
        public async Task<ClaudeResponse> SendTest([FromBody] ClaudeRequest request, CancellationToken cancellationToken)
        {
            return await _claudeService.SendRequest(request, cancellationToken);
        }

        [HttpPost]
        public async Task<ContentAIResponse> Send([FromBody] ContentAIRequest request, CancellationToken cancellationToken)
        {
            return await _contentService.SendRequest(request, cancellationToken);
        }

    }
}