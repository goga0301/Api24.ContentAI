﻿namespace Api24ContentAI.Domain.Entities
{
    public class Marketplace : BaseEntity
    {
        public string Name { get; set; }
        public int ContentLimit { get; set; }
        public int TranslateLimit { get; set; }
    }
}
