using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Service
{
    public interface ICustomTemplateService
    {
        Task<List<CustomTemplateModel>> GetAll(CancellationToken cancellationToken);
        Task<CustomTemplateModel> GetById(Guid id, CancellationToken cancellationToken);
        Task Create(CreateCustomTemplateModel customTemplate, CancellationToken cancellationToken);
        Task Update(UpdateCustomTemplateModel customTemplate, CancellationToken cancellationToken);
        Task Delete(Guid id, CancellationToken cancellationToken);
        Task<CustomTemplateModel> GetByMarketplaceAndProductCategoryId(Guid marketplaceId, Guid productCategoryId, CancellationToken cancellationToken);
    }
}
