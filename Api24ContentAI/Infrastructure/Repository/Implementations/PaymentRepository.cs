using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly ContentDbContext _dbContext;

        public PaymentRepository(ContentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task Create(Payment payments)
        {
            _dbContext.Payments.Add(payments);
            await _dbContext.SaveChangesAsync();
        }

        public async Task Delete(Payment payments)
        {
            _dbContext.Payments.Remove(payments);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<Payment>> GetAll()
        {
            var payments = await _dbContext.Payments.AsNoTracking().ToListAsync();
            return payments;
        }

        public async Task<Payment> GetByOrderId(string orderId)
        {
            var payment = await _dbContext.Payments.SingleOrDefaultAsync(p => p.OrderId == orderId);
            return payment;
        }

        public async Task SaveChanges() 
            => await _dbContext.SaveChangesAsync();
        
    }
}
