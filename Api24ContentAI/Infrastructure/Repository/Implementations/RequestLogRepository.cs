using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class RequestLogRepository : IRequestLogRepository
    {
        private readonly ContentDbContext _dbContext;

        public RequestLogRepository(ContentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<int> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs.Where(x => x.MarketplaceId == marketplaceId).CountAsync(cancellationToken);
        }
        
        public async Task<int> CountTranslatesByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs.Where(x => x.MarketplaceId == marketplaceId && x.RequestType == RequestType.Translate).CountAsync(cancellationToken);
        }
              
        public async Task<int> CountContentAIByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs.Where(x => x.MarketplaceId == marketplaceId && x.RequestType == RequestType.Content).CountAsync(cancellationToken);
        }

        public async Task<int> CountCopyrightAIByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs.Where(x => x.MarketplaceId == marketplaceId && x.RequestType == RequestType.Copyright).CountAsync(cancellationToken);
        }

        public async Task<int> CountVideoScriptByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs.Where(x => x.MarketplaceId == marketplaceId && x.RequestType == RequestType.VideoScript).CountAsync(cancellationToken);
        }

        public async Task Create(RequestLog entity, CancellationToken cancellationToken)
        {
            await _dbContext.Set<RequestLog>().AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public IQueryable<RequestLog> GetAll()
        {
            return _dbContext.RequestLogs.AsNoTracking();
        }

        public async Task<RequestLog> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _dbContext.RequestLogs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public IQueryable<RequestLog> GetByMarketplaceId(Guid marketplaceId)
        {
            return _dbContext.RequestLogs.AsNoTracking().Where(x => x.MarketplaceId == marketplaceId);
        }
    }
}
