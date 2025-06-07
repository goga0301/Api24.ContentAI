using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Models.Mappers;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
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
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("User ID cannot be null or empty", nameof(id));
            }

            try
            {
                // Check if user exists before attempting deletion
                var user = await _userRepository.GetById(id, cancellationToken);
                if (user == null)
                {
                    throw new InvalidOperationException($"User with ID '{id}' not found");
                }

                await _userRepository.Delete(id, cancellationToken);
            }
            catch (ArgumentException)
            {
                throw; // Re-throw validation errors
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw business logic errors
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete user with ID '{id}': {ex.Message}", ex);
            }
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
