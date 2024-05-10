using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class RequestLogController : ControllerBase
    {
        private readonly IRequestLogService _requestLogService;

        public RequestLogController(IRequestLogService requestLogService)
        {
            _requestLogService = requestLogService;
        }

        [HttpGet]
        public async Task<List<RequestLogModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _requestLogService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<RequestLogModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _requestLogService.GetById(id, cancellationToken);
        }
        
        [HttpGet("count/{marketplaceId}")]
        public async Task<int> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _requestLogService.CountByMarketplaceId(marketplaceId, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateRequestLogModel model, CancellationToken cancellationToken)
        {
            await _requestLogService.Create(model, cancellationToken);
        }
    }
}
