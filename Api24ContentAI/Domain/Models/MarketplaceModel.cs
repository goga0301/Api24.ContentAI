using System;

namespace Api24ContentAI.Domain.Models
{
    public class MarketplaceModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int ContentLimit { get; set; }
        public int TranslateLimit { get; set; }
        public int EnhanceTranslateLimit { get; set; }
        public int CopyrightLimit { get; set; }
        public int VideoScriptLimit { get; set; }
        public int LawyerLimit { get; set; }
    }
    public class CreateMarketplaceModel
    {
        public string Name { get; set; }
        public int ContentLimit { get; set; }
        public int TranslateLimit { get; set; }
        public int EnhanceTranslateLimit { get; set; }
        public int CopyrightLimit { get; set; }
        public int VideoScriptLimit { get; set; }
        public int LawyerLimit { get; set; }

    }
    public class UpdateMarketplaceModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int ContentLimit { get; set; }
        public int TranslateLimit { get; set; }
        public int EnhanceTranslateLimit { get; set; }
        public int CopyrightLimit { get; set; }
        public int VideoScriptLimit { get; set; }
        public int LawyerLimit { get; set; }

    }
}
