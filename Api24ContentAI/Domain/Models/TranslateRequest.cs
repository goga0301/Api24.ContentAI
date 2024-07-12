using System;

namespace Api24ContentAI.Domain.Models
{
    public class TranslateRequest
    {
        public string Description { get; set; }
        public int LanguageId { get; set; }
        public Guid UniqueKey { get; set; }
    }
    public class UserTranslateRequest
    {
        public string Description { get; set; }
        public int LanguageId { get; set; }
    }
}
