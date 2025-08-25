namespace Api24ContentAI.Domain.Models
{
    public class PayseraOptions
    {
        public int ProjectId { get; set; }
        public string SignPassword { get; set; }
        public string AcceptUrl { get; set; }
        public string CancelUrl { get; set; }
        public string CallbackUrl { get; set; }
        public bool IsTestMode { get; set; }
    }
}
