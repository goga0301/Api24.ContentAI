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
        private const int StartingBalance = 100;
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

        public async Task CreateUserBalance(string userId, CancellationToken cancellationToken)
        {
            await _context.UserBalances.AddAsync(new UserBalance
            {
                UserId = userId,
                Balance = StartingBalance
            });
            await _context.SaveChangesAsync(cancellationToken);
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
            return _context.Users.Include(x => x.UserBalance).Include(x => x.Role);
        }

        public async Task<User> GetById(string userId, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId);
        }

        public async Task<User> GetByUserName(string userName, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).Include(x => x.Role).SingleOrDefaultAsync(x => x.UserName == userName);
        }

        public async Task Update(User entity, CancellationToken cancellationToken)
        {
            //var user = await _context.Users.SingleOrDefaultAsync(x => x.Id == entity.Id);

            _context.Users.Update(entity);
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
