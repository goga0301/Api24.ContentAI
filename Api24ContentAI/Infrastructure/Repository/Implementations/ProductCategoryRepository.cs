using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class ProductCategoryRepository(ContentDbContext dbContext) : GenericRepository<ProductCategory>(dbContext), IProductCategoryRepository
    {
    }
}
