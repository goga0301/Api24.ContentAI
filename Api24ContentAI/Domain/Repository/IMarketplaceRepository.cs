using Api24ContentAI.Domain.Entities;
using System;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface IMarketplaceRepository : IGenericRepository<Marketplace>
    {
        Task UpdateBalance(Guid uniqueKey, RequestType requestType);
    }
}
