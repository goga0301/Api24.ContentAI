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
            };
        }

        public static MarketplaceModel ToModel(this Marketplace entity)
        {
            return new MarketplaceModel
            {
                Id = entity.Id,
                Name = entity.Name,
            };
        }
    }
}
