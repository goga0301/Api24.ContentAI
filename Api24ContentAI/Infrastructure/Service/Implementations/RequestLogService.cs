using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class RequestLogService : IRequestLogService
    {
        private readonly IRequestLogRepository _requestLogRepository;

        public RequestLogService(IRequestLogRepository requestLogRepository)
        {
            _requestLogRepository = requestLogRepository;
        }

        public async Task<int> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            return await _requestLogRepository.CountByMarketplaceId(marketplaceId, cancellationToken);
        }

        public async Task Create(CreateRequestLogModel model, CancellationToken cancellationToken)
        {
            await _requestLogRepository.Create(new RequestLog
            {
                Id = Guid.NewGuid(),
                MarketplaceId = model.MarketplaceId,
                RequestJson = JsonSerializer.Serialize(model.Request),
                CreateTime = DateTime.UtcNow,
            }, cancellationToken);
        }

        public async Task<List<RequestLogModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _requestLogRepository.GetAll().Select(x => new RequestLogModel
            {
                Id = x.Id,
                MarketplaceId = x.MarketplaceId,
                RequestJson = x.RequestJson,
                CreateTime = x.CreateTime
            }).ToListAsync(cancellationToken);
        }

        public async Task<RequestLogModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _requestLogRepository.GetById(id, cancellationToken);
            return new RequestLogModel
            {
                Id = entity.Id,
                MarketplaceId = entity.MarketplaceId,
                RequestJson = entity.RequestJson,
                CreateTime = entity.CreateTime
            };
        }
    }
}
