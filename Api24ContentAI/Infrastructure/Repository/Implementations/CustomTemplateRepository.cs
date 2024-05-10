using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class CustomTemplateRepository : GenericRepository<CustomTemplate>, ICustomTemplateRepository
    {
        public CustomTemplateRepository(ContentDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<CustomTemplate> GetByMarketplaceAndProductCategoryId(Guid marketplaceId, Guid productCategoryId, CancellationToken cancellationToken)
        {
            return await _dbContext.Set<CustomTemplate>()
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(e => e.MarketplaceId == marketplaceId && e.ProductCategoryId == productCategoryId, cancellationToken);
        }
    }
}
