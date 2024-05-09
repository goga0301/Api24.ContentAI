using System;

namespace Api24ContentAI.Domain.Models
{
    public class ProductCategoryModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
    public class CreateProductCategoryModel
    {
        public string Name { get; set; }
    }
    public class UpdateProductCategoryModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
