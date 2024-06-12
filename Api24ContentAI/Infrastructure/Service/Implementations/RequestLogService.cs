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

        public async Task<LogCountModel> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            var translateCount = await _requestLogRepository.CountTranslatesByMarketplaceId(marketplaceId, cancellationToken);
            var contentCount = await _requestLogRepository.CountContentAIByMarketplaceId(marketplaceId, cancellationToken);

            return new LogCountModel { ContentAICount = contentCount, TranslateCount = translateCount };
        }

        public async Task Create(CreateRequestLogModel model, CancellationToken cancellationToken)
        {
            await _requestLogRepository.Create(new RequestLog
            {
                Id = Guid.NewGuid(),
                MarketplaceId = model.MarketplaceId,
                RequestJson = model.Request,
                CreateTime = DateTime.UtcNow,
                RequestType = model.RequestType,
            }, cancellationToken);
        }

        public async Task<List<RequestLogModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _requestLogRepository.GetAll().Select(x => new RequestLogModel
            {
                Id = x.Id,
                MarketplaceId = x.MarketplaceId,
                RequestJson = x.RequestJson,
                CreateTime = x.CreateTime,
                RequestType = x.RequestType
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
                CreateTime = entity.CreateTime,
                RequestType = entity.RequestType
            };
        }

        public async Task<List<RequestLogModel>> GetByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken)
        {
            var t = await _requestLogRepository.GetByMarketplaceId(marketplaceId).Select(x => new RequestLogModel
            {
                Id = x.Id,
                MarketplaceId = x.MarketplaceId,
                RequestJson = x.RequestJson,
                CreateTime = x.CreateTime,
                RequestType = x.RequestType
            }).ToListAsync(cancellationToken);

            return t;
        }
    }
}
