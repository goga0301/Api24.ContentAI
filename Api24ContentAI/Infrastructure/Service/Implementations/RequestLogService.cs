using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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
            var copyrightCount = await _requestLogRepository.CountCopyrightAIByMarketplaceId(marketplaceId, cancellationToken);
            var videoScriptCount = await _requestLogRepository.CountVideoScriptByMarketplaceId(marketplaceId, cancellationToken);

            return new LogCountModel
            {
                ContentAICount = contentCount,
                TranslateCount = translateCount,
                CopyrightAICount = copyrightCount,
                VideoScriptCount = videoScriptCount
            };
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

    public class UserRequestLogService : IUserRequestLogService
    {
        private readonly IUserRequestLogRepository _requestLogRepository;

        public UserRequestLogService(IUserRequestLogRepository requestLogRepository)
        {
            _requestLogRepository = requestLogRepository;
        }

        public async Task<LogCountModel> CountByUserId(string UserId, CancellationToken cancellationToken)
        {
            var translateCount = await _requestLogRepository.CountTranslatesByUserId(UserId, cancellationToken);
            var contentCount = await _requestLogRepository.CountContentAIByUserId(UserId, cancellationToken);
            var copyrightCount = await _requestLogRepository.CountCopyrightAIByUserId(UserId, cancellationToken);
            var videoScriptCount = await _requestLogRepository.CountVideoScriptByUserId(UserId, cancellationToken);

            return new LogCountModel
            {
                ContentAICount = contentCount,
                TranslateCount = translateCount,
                CopyrightAICount = copyrightCount,
                VideoScriptCount = videoScriptCount
            };
        }

        public async Task Create(CreateUserRequestLogModel model, CancellationToken cancellationToken)
        {
            await _requestLogRepository.Create(new UserRequestLog
            {
                Id = Guid.NewGuid(),
                UserId = model.UserId,
                RequestJson = model.Request,
                ResponseJson = model.Response,
                CreateTime = DateTime.UtcNow,
                RequestType = model.RequestType,
            }, cancellationToken);
        }

        public async Task<List<UserRequestLogModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _requestLogRepository.GetAll().Where(x => !string.IsNullOrWhiteSpace(x.ResponseJson)).Select(x => new UserRequestLogModel
            {
                Id = x.Id,
                UserId = x.UserId,
                RequestJson = x.RequestJson,
                ResponseJson = x.ResponseJson,
                CreateTime = x.CreateTime,
                RequestType = x.RequestType
            }).ToListAsync(cancellationToken);
        }

        public async Task<UserRequestLogModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            var entity = await _requestLogRepository.GetById(id, cancellationToken);
            return new UserRequestLogModel
            {
                Id = entity.Id,
                UserId = entity.UserId,
                RequestJson = entity.RequestJson,
                ResponseJson = entity.ResponseJson,
                CreateTime = entity.CreateTime,
                RequestType = entity.RequestType
            };
        }

        public async Task<List<UserRequestLogModel>> GetByUserId(string UserId, CancellationToken cancellationToken)
        {
            var t = await _requestLogRepository.GetByUserId(UserId).Where(x => !string.IsNullOrWhiteSpace(x.ResponseJson)).Select(x => new UserRequestLogModel
            {
                Id = x.Id,
                UserId = x.UserId,
                RequestJson = x.RequestJson,
                ResponseJson = x.ResponseJson,
                CreateTime = x.CreateTime,
                RequestType = x.RequestType
            }).ToListAsync(cancellationToken);

            return t;
        }
    }
}
