using Api24ContentAI.Domain.Entities;
using System;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class TemplateMappers
    {
        public static Template ToEntity(this CreateTemplateModel model)
        {
            return new Template
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
                Text = model.Text,
                Language = model.Language,
                ProductCategoryId = model.ProductCategoryId,
            };
        }

        public static TemplateModel ToModel(this Template entity)
        {
            return new TemplateModel
            {
                Id = entity.Id,
                Name = entity.Name,
                Text = entity.Text,
                Language = entity.Language,
                ProductCategoryId = entity.ProductCategoryId,
            };
        }
    }
}
