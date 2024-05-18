using Api24ContentAI.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface ICustomTemplateRepository : IGenericRepository<CustomTemplate>
    {
        Task<CustomTemplate> GetByMarketplaceAndProductCategoryId(Guid marketplaceId, Guid productCategoryId, CancellationToken cancellationToken);
        Task<CustomTemplate> GetByMarketplaceAndProductCategoryIdAndLanguage(Guid marketplaceId, Guid productCategoryId, string language, CancellationToken cancellationToken);
    }
}
