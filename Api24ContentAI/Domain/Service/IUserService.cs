using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Service
{
    public interface IUserService
    {
        Task<List<UserModel>> GetAll(CancellationToken cancellationToken);
        Task<UserModel> GetById(string id, CancellationToken cancellationToken);
        Task Update(UpdateUserModel user, CancellationToken cancellationToken);
        Task<bool> ChangePassword(ChangeUserPasswordModel model, CancellationToken cancellationToken);
        Task Delete(string id, CancellationToken cancellationToken);
    }
}
