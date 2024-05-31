using Api24ContentAI.Domain.Entities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System;

namespace Api24ContentAI.Domain.Models
{
    public class RequestLogModel
    {
        public Guid Id { get; set; }
        public Guid MarketplaceId { get; set; }
        public string RequestJson { get; set; }
        public DateTime CreateTime { get; set; }
        public RequestType RequestType { get; set; }
    }
    public class CreateRequestLogModel
    {
        public Guid MarketplaceId { get; set; }
        public string Request { get; set; }
        public RequestType RequestType { get; set; }
    }
}
