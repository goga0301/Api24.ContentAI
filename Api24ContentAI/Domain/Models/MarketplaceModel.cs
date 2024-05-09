using System;

namespace Api24ContentAI.Domain.Models
{
    public class MarketplaceModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
    public class CreateMarketplaceModel
    {
        public string Name { get; set; }
    }
    public class UpdateMarketplaceModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
