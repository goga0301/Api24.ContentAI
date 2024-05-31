﻿using System;

namespace Api24ContentAI.Domain.Entities
{
    public class RequestLog : BaseEntity
    {
        public Guid MarketplaceId { get; set; }
        public string RequestJson { get; set; }
        public DateTime CreateTime { get; set; }
        public RequestType RequestType { get; set; }
    }
    public enum RequestType
    {
        Content = 1,
        Translate = 2
    }
}
