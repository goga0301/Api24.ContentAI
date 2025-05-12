using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class GenericRepository<TEntity>(ContentDbContext dbContext) : IGenericRepository<TEntity> where TEntity : BaseEntity
    {
        protected readonly ContentDbContext _dbContext = dbContext;

        public IQueryable<TEntity> GetAll()
        {
            return _dbContext.Set<TEntity>().AsNoTracking();
        }

        public async Task<TEntity> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _dbContext.Set<TEntity>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public async Task<Guid> Create(TEntity entity, CancellationToken cancellationToken)
        {
            _ = await _dbContext.Set<TEntity>().AddAsync(entity, cancellationToken);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }

        public async Task Update(TEntity entity, CancellationToken cancellationToken)
        {
            _ = _dbContext.Set<TEntity>().Update(entity);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            TEntity entity = await GetById(id, cancellationToken);
            _ = _dbContext.Set<TEntity>().Remove(entity);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
