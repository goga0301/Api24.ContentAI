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
    public class RequestLogController(IRequestLogService requestLogService) : ControllerBase
    {
        private readonly IRequestLogService _requestLogService = requestLogService;

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

        [HttpGet("by-marketplace/{marketplaceId}")]
        public async Task<List<RequestLogModel>> GetByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _requestLogService.GetByMarketplaceId(marketplaceId, cancellationToken);
        }

        [HttpGet("count/{marketplaceId}")]
        public async Task<LogCountModel> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _requestLogService.CountByMarketplaceId(marketplaceId, cancellationToken);
        }
    }
}
