using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IPayseraService
    {
        Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request);
        Task<bool> ValidateCallbackAsync(Dictionary<string, string> parameters);
        Task<PaymentStatus> ProcessCallbackAsync(Dictionary<string, string> parameters);
    }
}
