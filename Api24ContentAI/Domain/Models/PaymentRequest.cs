namespace Api24ContentAI.Domain.Models
{
    public class PaymentRequest
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Country { get; set; }
    }
}
