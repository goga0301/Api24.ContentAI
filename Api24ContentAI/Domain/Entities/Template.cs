using System;

namespace Api24ContentAI.Domain.Entities
{
    public class Template : BaseEntity
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public string Language { get; set; }
        public Guid ProductCategoryId { get; set; } 
        public ProductCategory ProductCategory { get; set; }
    }
}
