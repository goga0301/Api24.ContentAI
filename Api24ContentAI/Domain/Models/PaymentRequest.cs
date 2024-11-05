namespace Api24ContentAI.Domain.Models
{
    public class PaymentRequest
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Country { get; set; }
    }
}
