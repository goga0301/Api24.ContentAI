using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class LanguageRepository(ContentDbContext dbContext) : ILanguageRepository
    {
        private readonly ContentDbContext _dbContext = dbContext;

        public IQueryable<Language> GetAll()
        {
            return _dbContext.Set<Language>().AsNoTracking();
        }

        public async Task<Language> GetById(int id, CancellationToken cancellationToken)
        {
            return await _dbContext.Set<Language>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public async Task Create(Language entity, CancellationToken cancellationToken)
        {
            _ = await _dbContext.Set<Language>().AddAsync(entity, cancellationToken);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task Update(Language entity, CancellationToken cancellationToken)
        {
            _ = _dbContext.Set<Language>().Update(entity);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            Language entity = await GetById(id, cancellationToken);
            _ = _dbContext.Set<Language>().Remove(entity);
            _ = await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
