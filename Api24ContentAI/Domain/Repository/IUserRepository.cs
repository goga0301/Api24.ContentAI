using Api24ContentAI.Domain.Entities;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Api24ContentAI.Domain.Repository
{
    public interface IUserRepository 
    {
        IQueryable<User> GetAll();

        Task<User> GetByUserName(string userName, CancellationToken cancellationToken);
        Task<User> GetById(string userId, CancellationToken cancellationToken);

        Task Create(User entity, CancellationToken cancellationToken);

        Task Update(User entity, CancellationToken cancellationToken);
        Task UpdateUserBalance(string userId, decimal newBalance, CancellationToken cancellationToken);

        Task Delete(string id, CancellationToken cancellationToken);
    }
}
