using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Models.Mappers;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly UserManager<User> _userManager;

        public UserService(IUserRepository userRepository, UserManager<User> userManager)
        {
            _userRepository = userRepository;
            _userManager = userManager;
        }

        public async Task Delete(string id, CancellationToken cancellationToken)
        {
            await _userRepository.Delete(id, cancellationToken);
        }

        public async Task<List<UserModel>> GetAll(CancellationToken cancellationToken)
        {
            return (await _userRepository.GetAll().ToListAsync()).Select(x => x.ToModel()).ToList();
        }
        
        public async Task<UserModel> GetById(string id, CancellationToken cancellationToken)
        {
            return (await _userRepository.GetById(id, cancellationToken)).ToModel();
        }

        public async Task<UserModel> GetByUserName(string userName, CancellationToken cancellationToken)
        {
            return (await _userRepository.GetByUserName(userName, cancellationToken)).ToModel();
        }

        public async Task Update(UpdateUserModel user, CancellationToken cancellationToken)
        {

            User entity = await _userRepository.GetById(user.Id, cancellationToken);
            if (entity == null)
            {
                throw new System.Exception($"User not found by id: {user.Id}");
            }

            entity.UserName = user.UserName;
            entity.Email = user.Email;
            entity.PhoneNumber = user.PhoneNUmber;
            entity.FirstName = user.FirstName;
            entity.LastName = user.LastName;
            entity.RoleId = user.RoleId;
            entity.UserBalance.Balance = user.Balance;
            await _userRepository.Update(entity, cancellationToken);
        }
        public async Task<bool> ChangePassword(ChangeUserPasswordModel model, CancellationToken cancellationToken)
        {
            User user = await _userRepository.GetById(model.Id, cancellationToken);
            IdentityResult result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            return result.Succeeded;
        }
    }
}
