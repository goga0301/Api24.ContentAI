﻿using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using Api24ContentAI.Domain.Models;

namespace Api24ContentAI.Domain.Service
{
    public interface IRequestLogService
    {
        Task<List<RequestLogModel>> GetAll(CancellationToken cancellationToken);

        Task<RequestLogModel> GetById(Guid id, CancellationToken cancellationToken);

        Task Create(CreateRequestLogModel model, CancellationToken cancellationToken);

        Task<LogCountModel> CountByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
        Task<List<RequestLogModel>> GetByMarketplaceId(Guid marketplaceId, CancellationToken cancellationToken);
    }
}
