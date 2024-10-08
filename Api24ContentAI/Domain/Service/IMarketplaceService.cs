﻿using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Domain.Service
{
    public interface IMarketplaceService
    {
        Task<List<MarketplaceModel>> GetAll(CancellationToken cancellationToken);
        Task<MarketplaceModel> GetById(Guid id, CancellationToken cancellationToken);
        Task<Guid> Create(CreateMarketplaceModel marketplace, CancellationToken cancellationToken);
        Task Update(UpdateMarketplaceModel marketplace, CancellationToken cancellationToken);
        Task Delete(Guid id, CancellationToken cancellationToken);

        Task UpdateBalance(Guid uniqueKey, RequestType requestType);

    }
}
