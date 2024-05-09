using System;
using System.Collections.Generic;

namespace Api24ContentAI.Domain.Models
{
    public class ContentAIRequest
    {
        public string ProductName { get; set; }
        public Guid ProductCategoryId { get; set; }
        public Guid UniqueKey { get; set; }
        public List<Attribute> Attributes { get; set; }
    }

    public class Attribute
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
