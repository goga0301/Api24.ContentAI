using Api24ContentAI.Domain.Entities;
using System;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class MarketplaceMappers
    {
        public static Marketplace ToEntity(this CreateMarketplaceModel model)
        {
            return new Marketplace
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
                ContentLimit = model.ContentLimit,
                TranslateLimit = model.TranslateLimit,
                EnhanceTranslateLimit = model.EnhanceTranslateLimit,
                CopyrightLimit = model.CopyrightLimit,
                VideoScriptLimit = model.VideoScriptLimit,
                LawyerLimit = model.LawyerLimit
            };
        }

        public static MarketplaceModel ToModel(this Marketplace entity)
        {
            return new MarketplaceModel
            {
                Id = entity.Id,
                Name = entity.Name,
                ContentLimit = entity.ContentLimit,
                TranslateLimit = entity.TranslateLimit,
                EnhanceTranslateLimit = entity.EnhanceTranslateLimit,
                CopyrightLimit = entity.CopyrightLimit,
                VideoScriptLimit = entity.VideoScriptLimit,
                LawyerLimit = entity.LawyerLimit
            };
        }
    }
}
