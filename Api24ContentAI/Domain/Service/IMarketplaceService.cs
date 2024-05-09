using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Service
{
    public interface IMarketplaceService
    {
        Task<List<MarketplaceModel>> GetAll(CancellationToken cancellationToken);
        Task<MarketplaceModel> GetById(Guid id, CancellationToken cancellationToken);
        Task Create(CreateMarketplaceModel marketplace, CancellationToken cancellationToken);
        Task Update(UpdateMarketplaceModel marketplace, CancellationToken cancellationToken);
        Task Delete(Guid id, CancellationToken cancellationToken);
    }
}
