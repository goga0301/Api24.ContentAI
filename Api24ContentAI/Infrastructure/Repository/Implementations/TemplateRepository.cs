using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class TemplateRepository : GenericRepository<Template>, ITemplateRepository
    {
        public TemplateRepository(ContentDbContext dbContext) : base(dbContext)
        {
        }

        public async Task<Template> GetByProductCategoryId(Guid productCategoryId, CancellationToken cancellationToken)
        {
            return await _dbContext.Set<Template>()
                                              .AsNoTracking()
                                              .FirstOrDefaultAsync(e => e.ProductCategoryId == productCategoryId, cancellationToken);
        }
    }
}
