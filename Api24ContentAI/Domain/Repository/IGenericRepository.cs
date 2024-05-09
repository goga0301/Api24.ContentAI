using Api24ContentAI.Domain.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface IGenericRepository<TEntity> where TEntity : BaseEntity
    {
        IQueryable<TEntity> GetAll();

        Task<TEntity> GetById(Guid id, CancellationToken cancellationToken);

        Task Create(TEntity entity, CancellationToken cancellationToken);

        Task Update(TEntity entity, CancellationToken cancellationToken);

        Task Delete(Guid id, CancellationToken cancellationToken);
    }
}
