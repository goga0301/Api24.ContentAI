using Api24ContentAI.Domain.Models;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IPayseraService
    {
        Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request);
    }
}
