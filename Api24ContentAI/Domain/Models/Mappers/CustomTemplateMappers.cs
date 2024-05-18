using Api24ContentAI.Domain.Entities;
using System;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class CustomTemplateMappers
    {
        public static CustomTemplate ToEntity(this CreateCustomTemplateModel model)
        {
            return new CustomTemplate
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
                Text = model.Text,
                Language = model.Language,
                ProductCategoryId = model.ProductCategoryId,
                MarketplaceId = model.MarketplaceId,
            };
        }

        public static CustomTemplateModel ToModel(this CustomTemplate entity)
        {
            return new CustomTemplateModel
            {
                Id = entity.Id,
                Name = entity.Name,
                Text = entity.Text,
                Language = entity.Language,
                ProductCategoryId = entity.ProductCategoryId,
                MarketplaceId = entity.MarketplaceId,
            };
        }
    }
}
