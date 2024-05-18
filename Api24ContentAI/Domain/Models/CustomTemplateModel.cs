using System;

namespace Api24ContentAI.Domain.Models
{
    public class CustomTemplateModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public string Language { get; set; }
        public Guid ProductCategoryId { get; set; }
        public Guid MarketplaceId { get; set; }
    }

    public class CreateCustomTemplateModel
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public string Language { get; set; }
        public Guid ProductCategoryId { get; set; }
        public Guid MarketplaceId { get; set; }
    }

    public class UpdateCustomTemplateModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public string Language { get; set; }
        public Guid ProductCategoryId { get; set; }
        public Guid MarketplaceId { get; set; }
    }
}
