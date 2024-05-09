using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class ProductCategoryRepository : GenericRepository<ProductCategory>, IProductCategoryRepository
    {
        public ProductCategoryRepository(ContentDbContext dbContext) : base(dbContext)
        {
        }
    }
}
