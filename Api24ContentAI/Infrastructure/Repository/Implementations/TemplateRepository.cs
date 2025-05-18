using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class TemplateRepository(ContentDbContext dbContext) : GenericRepository<Template>(dbContext), ITemplateRepository
    {


        public async Task<Template> GetByProductCategoryId(Guid productCategoryId, CancellationToken cancellationToken)
        {
            return await _dbContext.Set<Template>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.ProductCategoryId == productCategoryId, cancellationToken);
        }

        //public async Task<Template> GetByProductCategoryIdAndLanguage(Guid productCategoryId, string language, CancellationToken cancellationToken)
        //{
        //    return await _dbContext.Set<Template>()
        //                                                .AsNoTracking()
        //                                                .FirstOrDefaultAsync(e => e.ProductCategoryId == productCategoryId && e.Language == language, cancellationToken);
        //}
    }
}
