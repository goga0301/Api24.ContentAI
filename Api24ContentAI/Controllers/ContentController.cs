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
        private readonly IContentService _contentService;

        public ContentController(IContentService contentService)
        {
            _contentService = contentService;
        }

        [HttpPost]
        public async Task<ContentAIResponse> Send([FromBody] ContentAIRequest request, CancellationToken cancellationToken)
        {
            return await _contentService.SendRequest(request, cancellationToken);
        }

    }
}