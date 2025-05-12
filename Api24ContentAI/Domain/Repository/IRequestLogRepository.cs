using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Domain.Repository
{
    public interface IRequestLogRepository
    {
        IQueryable<RequestLog> GetAll();

        Task<RequestLog> GetById(Guid id, CancellationToken cancellationToken);

        Task Create(RequestLog entity, CancellationToken cancellationToken);

        Task<int> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        Task<int> CountTranslatesByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        Task<int> CountContentAIByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        Task<int> CountCopyrightAIByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        Task<int> CountVideoScriptByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        IQueryable<RequestLog> GetByMarketplaceId(Guid marketplaceId);
    }
}
