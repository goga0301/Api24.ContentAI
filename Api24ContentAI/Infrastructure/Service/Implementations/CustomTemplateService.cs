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

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class CustomTemplateService : ICustomTemplateService
    {
        private readonly ICustomTemplateRepository _customTemplateRepository;

        public CustomTemplateService(ICustomTemplateRepository customTemplateRepository)
        {
            _customTemplateRepository = customTemplateRepository;
        }

        public async Task<Guid> Create(CreateCustomTemplateModel customTemplate, CancellationToken cancellationToken)
        {
            return await _customTemplateRepository.Create(customTemplate.ToEntity(), cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _customTemplateRepository.Delete(id, cancellationToken);
        }

        public async Task<List<CustomTemplateModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _customTemplateRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<CustomTemplateModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return (await _customTemplateRepository.GetById(id, cancellationToken)).ToModel();
        }

        public async Task<CustomTemplateModel> GetByMarketplaceAndProductCategoryId(Guid marketplaceId, Guid productCategoryId, CancellationToken cancellationToken)
        {
            var entity = await _customTemplateRepository.GetByMarketplaceAndProductCategoryId(marketplaceId, productCategoryId, cancellationToken);
            if (entity == null) return null;
            return entity.ToModel();
        }

        public async Task<CustomTemplateModel> GetByMarketplaceAndProductCategoryIdAndLanguage(Guid marketplaceId, Guid productCategoryId, string language, CancellationToken cancellationToken)
        {
            var entity = await _customTemplateRepository.GetByMarketplaceAndProductCategoryIdAndLanguage(marketplaceId, productCategoryId, language, cancellationToken);
            if (entity == null) return null;
            return entity.ToModel();
        }

        public async Task Update(UpdateCustomTemplateModel customTemplate, CancellationToken cancellationToken)
        {
            var entity = await _customTemplateRepository.GetById(customTemplate.Id, cancellationToken);
            entity.Name = customTemplate.Name;
            entity.Text = customTemplate.Text;
            entity.MarketplaceId = customTemplate.MarketplaceId;
            entity.ProductCategoryId = customTemplate.ProductCategoryId;

            await _customTemplateRepository.Update(entity, cancellationToken);
        }
    }
}
