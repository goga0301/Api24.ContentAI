using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class UserRepository : IUserRepository
    {
        private const int StartingBalance = 25;
        private readonly ContentDbContext _context;
        private readonly string connectionString;

        public UserRepository(ContentDbContext context, IConfiguration configuration)
        {
            _context = context;
            connectionString = configuration.GetSection("DatabaseOptions:ConnectionString").Value ?? "";
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
            var userRequestLogs = await _context.UserRequestLogs
                .Where(x => x.UserId == id)
                .ToListAsync(cancellationToken);
            
            if (userRequestLogs.Any())
            {
                _context.UserRequestLogs.RemoveRange(userRequestLogs);
            }

            UserBalance balancEntity = await _context.UserBalances
                .SingleOrDefaultAsync(x => x.UserId == id, cancellationToken);
            
            if (balancEntity != null)
            {
                _context.UserBalances.Remove(balancEntity);
            }

            User entity = await _context.Users
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            
            if (entity != null)
            {
                _context.Users.Remove(entity);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public IQueryable<User> GetAll()
        {
            return _context.Users.Include(x => x.UserBalance).Include(x => x.Role);
        }

        public async Task<User> GetById(string userId, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken: cancellationToken);
        }

        public async Task<User> GetByUserName(string userName, CancellationToken cancellationToken)
        {
            return await _context.Users.Include(x => x.UserBalance).Include(x => x.Role).SingleOrDefaultAsync(x => x.UserName == userName, cancellationToken: cancellationToken);
        }

        public async Task Update(User entity, CancellationToken cancellationToken)
        {
            //var user = await _context.Users.SingleOrDefaultAsync(x => x.Id == entity.Id);

            _context.Users.Update(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateUserBalance(string userId, decimal price, CancellationToken cancellationToken)
        {
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();

                UserBalance balance = await connection.QuerySingleOrDefaultAsync<UserBalance>(
                    @"select ""UserId"" from ""ContentDb"".""UserBalances"" WHERE ""UserId"" = @UserId",
                    new { UserId = userId });

                if (balance != null)
                {
                    string updateQuery = @"UPDATE ""ContentDb"".""UserBalances""
                                    	SET ""Balance""=""Balance"" - @Price
                                    	WHERE ""UserId"" = @UserId";

                    await connection.ExecuteAsync(updateQuery, new { Price = price, UserId = userId });
                }
            }
        }
    }
}
