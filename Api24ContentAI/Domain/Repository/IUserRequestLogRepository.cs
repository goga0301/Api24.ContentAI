using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Domain.Repository
{
    public interface IUserRequestLogRepository
    {
        IQueryable<UserRequestLog> GetAll();

        Task<UserRequestLog> GetById(Guid id, CancellationToken cancellationToken);

        Task Create(UserRequestLog entity, CancellationToken cancellationToken);

        Task<int> CountByUserId(string userId, CancellationToken cancellationToken);
        Task<int> CountTranslatesByUserId(string userId, CancellationToken cancellationToken);
        Task<int> CountContentAIByUserId(string userId, CancellationToken cancellationToken);
        Task<int> CountCopyrightAIByUserId(string userId, CancellationToken cancellationToken);
        Task<int> CountVideoScriptByUserId(string userId, CancellationToken cancellationToken);
        IQueryable<UserRequestLog> GetByUserId(string userId);
    }
}
