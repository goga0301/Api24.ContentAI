using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Service
{
    public interface IProductCategoryService
    {
        Task<List<ProductCategoryModel>> GetAll(CancellationToken cancellationToken);
        Task<ProductCategoryModel> GetById(Guid id, CancellationToken cancellationToken);
        Task<Guid> Create(CreateProductCategoryModel productCategory, CancellationToken cancellationToken);
        Task Update(UpdateProductCategoryModel productCategory, CancellationToken cancellationToken);
        Task Delete(Guid id, CancellationToken cancellationToken);
        Task SyncCategories(CancellationToken cancellationToken);
    }
}
