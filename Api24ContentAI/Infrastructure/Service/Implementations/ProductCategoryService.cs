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
    public class ProductCategoryService : IProductCategoryService
    {
        private readonly IProductCategoryRepository _productCategoryRepository;

        public ProductCategoryService(IProductCategoryRepository productCategoryRepository)
        {
            _productCategoryRepository = productCategoryRepository;
        }

        public async Task Create(CreateProductCategoryModel productCategory, CancellationToken cancellationToken)
        {
            await _productCategoryRepository.Create(productCategory.ToEntity(), cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _productCategoryRepository.Delete(id, cancellationToken);
        }

        public async Task<List<ProductCategoryModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _productCategoryRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<ProductCategoryModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return (await _productCategoryRepository.GetById(id, cancellationToken)).ToModel();
        }

        public async Task Update(UpdateProductCategoryModel productCategory, CancellationToken cancellationToken)
        {
            var entity = await _productCategoryRepository.GetById(productCategory.Id, cancellationToken);
            entity.Name = productCategory.Name;

            await _productCategoryRepository.Update(entity, cancellationToken);
        }
    }
}
