using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using Api24ContentAI.Domain.Models;

namespace Api24ContentAI.Domain.Service
{
    public interface IUserRequestLogService
    {
        Task<List<UserRequestLogModel>> GetAll(CancellationToken cancellationToken);

        Task<UserRequestLogModel> GetById(Guid id, CancellationToken cancellationToken);

        Task Create(CreateUserRequestLogModel model, CancellationToken cancellationToken);

        Task<LogCountModel> CountByUserId(string UserId, CancellationToken cancellationToken);
        Task<List<UserRequestLogModel>> GetByUserId(string UserId, CancellationToken cancellationToken);
    }
}
