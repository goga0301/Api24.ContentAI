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
    public class UserRequestLogRepository : IUserRequestLogRepository
    {
        private readonly ContentDbContext _dbContext;

        public UserRequestLogRepository(ContentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<int> CountByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs.Where(x => x.UserId == userId).CountAsync(cancellationToken);
        }

        public async Task<int> CountTranslatesByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs.Where(x => x.UserId == userId && x.RequestType == RequestType.Translate).CountAsync(cancellationToken);
        }

        public async Task<int> CountContentAIByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs.Where(x => x.UserId == userId && x.RequestType == RequestType.Content).CountAsync(cancellationToken);
        }

        public async Task<int> CountCopyrightAIByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs.Where(x => x.UserId == userId && x.RequestType == RequestType.Copyright).CountAsync(cancellationToken);
        }

        public async Task<int> CountVideoScriptByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs.Where(x => x.UserId == userId && x.RequestType == RequestType.VideoScript).CountAsync(cancellationToken);
        }

        public async Task Create(UserRequestLog entity, CancellationToken cancellationToken)
        {
            await _dbContext.Set<UserRequestLog>().AddAsync(entity, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public IQueryable<UserRequestLog> GetAll()
        {
            return _dbContext.UserRequestLogs.AsNoTracking();
        }

        public async Task<UserRequestLog> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _dbContext.UserRequestLogs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        public IQueryable<UserRequestLog> GetByUserId(string userId)
        {
            return _dbContext.UserRequestLogs.AsNoTracking().Where(x => x.UserId == userId);
        }
    }
}
