using System;

namespace Api24ContentAI.Domain.Models
{
    public class TemplateModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public Guid ProductCategoryId { get; set; }
    }

    public class CreateTemplateModel
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public Guid ProductCategoryId { get; set; }
    }

    public class UpdateTemplateModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public Guid ProductCategoryId { get; set; }
    }
}
