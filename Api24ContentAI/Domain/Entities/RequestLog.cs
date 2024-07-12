using System;

namespace Api24ContentAI.Domain.Entities
{
    public class RequestLog : BaseEntity
    {
        public Guid MarketplaceId { get; set; }
        public string RequestJson { get; set; }
        public DateTime CreateTime { get; set; }
        public RequestType RequestType { get; set; }
    }  
    
    public class UserRequestLog : BaseEntity
    {
        public string UserId { get; set; }
        public string RequestJson { get; set; }
        public DateTime CreateTime { get; set; }
        public RequestType RequestType { get; set; }
    }
    public enum RequestType
    {
        Content = 1,
        Translate = 2,
        Copyright = 3,
        VideoScript = 4
    }
}
