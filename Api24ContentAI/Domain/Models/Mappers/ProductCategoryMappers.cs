using Api24ContentAI.Domain.Entities;
using System;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class ProductCategoryMappers
    {
        public static ProductCategory ToEntity(this CreateProductCategoryModel model)
        {
            return new ProductCategory
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
            };
        }

        public static ProductCategoryModel ToModel(this ProductCategory entity)
        {
            return new ProductCategoryModel
            {
                Id = entity.Id,
                Name = entity.Name,
            };
        }
    }
}
