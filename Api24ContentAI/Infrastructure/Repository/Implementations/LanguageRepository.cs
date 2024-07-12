using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class LanguageRepository : ILanguageRepository
    {
        private readonly ContentDbContext _dbContext;

        public LanguageRepository(ContentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

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
            await _dbContext.Set<Language>().AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task Update(Language entity, CancellationToken cancellationToken)
        {
            _dbContext.Set<Language>().Update(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            var entity = await GetById(id, cancellationToken);
            _dbContext.Set<Language>().Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
