using Api24ContentAI.Domain.Models;
using System;

namespace Api24ContentAI.Domain.Entities
{
    public class Payment : BaseEntity
    {
        public string OrderId { get; set; }
        public int? Amount { get; set; }
        public string Currency { get; set; }
        public string Country { get; set; }
        public PaymentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string UserId { get; set; }
        public User User { get; set; }
    }
}
