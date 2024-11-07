using Api24ContentAI.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface IPaymentRepository
    {
        Task<IEnumerable<Payment>> GetAll();
        Task<Payment> GetByOrderId(string id);
        Task Delete(Payment payments);
        Task Create(Payment payments);
        Task SaveChanges();
    }
}
