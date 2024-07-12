using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class UserRepository : IUserRepository
    {
        private readonly ContentDbContext _context;

        public UserRepository(ContentDbContext context)
        {
            _context = context;
        }

        public async Task Create(User entity, CancellationToken cancellationToken)
        {
            await _context.Users.AddAsync(entity, cancellationToken);
            await _context.SaveChangesAsync();
        }

        public async Task Delete(string id, CancellationToken cancellationToken)
        {
            var balancEntity = await _context.UserBalances.SingleOrDefaultAsync(x => x.UserId == id);
            _context.UserBalances.Remove(balancEntity);

            var entity = await _context.Users.SingleOrDefaultAsync(x => x.Id == id);
            _context.Users.Remove(entity);

            await _context.SaveChangesAsync();
        }

        public IQueryable<User> GetAll()
        {
            return _context.Users.Include(x => x.UserBalance);
        }

        public async Task<User> GetById(string userId, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).SingleOrDefaultAsync(x => x.Id == userId);
        }

        public async Task<User> GetByUserName(string userName, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).SingleOrDefaultAsync(x => x.UserName == userName);
        }

        public async Task Update(User entity, CancellationToken cancellationToken)
        {
            var user = await _context.Users.SingleOrDefaultAsync(x => x.Id == entity.Id);

            user.FirstName = entity.FirstName;
            user.LastName = entity.LastName;
            user.UserBalance.Balance = entity.UserBalance.Balance;

            _context.Users.Update(user);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateUserBalance(string userId, decimal newBalance, CancellationToken cancellationToken)
        {
            var balance = await _context.UserBalances.SingleOrDefaultAsync(x => x.UserId == userId);
            balance.Balance = newBalance;
            _context.UserBalances.Update(balance);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
