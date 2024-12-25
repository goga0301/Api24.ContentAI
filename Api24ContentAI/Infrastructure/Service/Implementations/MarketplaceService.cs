using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Models.Mappers;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class MarketplaceService : IMarketplaceService
    {
        private readonly IMarketplaceRepository _marketplaceRepository;

        public MarketplaceService(IMarketplaceRepository marketplaceRepository)
        {
            _marketplaceRepository = marketplaceRepository;
        }

        public async Task<Guid> Create(CreateMarketplaceModel marketplace, CancellationToken cancellationToken)
        {
            return await _marketplaceRepository.Create(marketplace.ToEntity(), cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _marketplaceRepository.Delete(id, cancellationToken);
        }

        public async Task<List<MarketplaceModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _marketplaceRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<MarketplaceModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            var marketplace = await _marketplaceRepository.GetById(id, cancellationToken);
            if (marketplace == null)
            {
                throw new Exception("მარკეტფლეისი არ მოიძებნა");
            }
            return (await _marketplaceRepository.GetById(id, cancellationToken)).ToModel();
        }

        public async Task Update(UpdateMarketplaceModel marketplace, CancellationToken cancellationToken)
        {
            var entity = await _marketplaceRepository.GetById(marketplace.Id, cancellationToken);
            entity.Name = marketplace.Name;
            entity.TranslateLimit = marketplace.TranslateLimit;
            entity.ContentLimit = marketplace.ContentLimit;
            entity.CopyrightLimit = marketplace.CopyrightLimit;
            entity.VideoScriptLimit = marketplace.VideoScriptLimit;
            entity.LawyerLimit = marketplace.LawyerLimit;
            entity.EnhanceTranslateLimit = marketplace.EnhanceTranslateLimit;

            await _marketplaceRepository.Update(entity, cancellationToken);
        }

        public async Task UpdateBalance(Guid uniqueKey, RequestType requestType)
        {
            await _marketplaceRepository.UpdateBalance(uniqueKey, requestType);
        }
    }
}
