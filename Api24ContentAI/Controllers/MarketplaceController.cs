using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Threading;
using Api24ContentAI.Domain.Service;
using System.Collections.Generic;
using Api24ContentAI.Domain.Models;
using System;
using Microsoft.AspNetCore.Authorization;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class MarketplaceController : ControllerBase
    {
        private readonly IMarketplaceService _marketplaceService;

        public MarketplaceController(IMarketplaceService marketplaceService)
        {
            _marketplaceService = marketplaceService;
        }

        [HttpGet]
        public async Task<List<MarketplaceModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _marketplaceService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<MarketplaceModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _marketplaceService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateMarketplaceModel model, CancellationToken cancellationToken)
        {
            await _marketplaceService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateMarketplaceModel model, CancellationToken cancellationToken)
        {
            await _marketplaceService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _marketplaceService.Delete(id, cancellationToken);
        }
    }
}
