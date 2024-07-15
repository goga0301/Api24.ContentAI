﻿using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Api24ContentAI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using Api24ContentAI.Domain.Repository;
using System.Threading;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly ContentDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly IJwtGenerator _jwtTokenGenerator;
        private readonly IUserRepository _userRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string _adminRole = "administrator";
        private const string _customerRole = "user";


        public AuthService(ContentDbContext context,
                           UserManager<User> userManager,
                           RoleManager<Role> roleManager,
                           IUserRepository userRepository,
                           IJwtGenerator jwtTokenGenerator,
                           IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtTokenGenerator = jwtTokenGenerator;
            _httpContextAccessor = httpContextAccessor;
            _userRepository = userRepository;
        }
        public async Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.UserName.ToLower() == loginRequest.UserName.ToLower());
            if(user == null)
            {
                throw new Exception($"User not found by username {loginRequest.UserName}");
            }
            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequest.Password);

            if (!isValid)
            {
                throw new Exception("Password is not correct");
            }
            var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);

            var token = _jwtTokenGenerator.GenerateToken(user, new List<string>() { role.NormalizedName }); //gaaketa tokeni


            return new LoginResponse()
            {
                Token = token
            };
        }

        public async Task Register(RegistrationRequest registrationRequest, CancellationToken cancellationToken)
        {
            // identityuser unda shevkra amisgan
            User user = new()
            {
                UserName = registrationRequest.Email,
                NormalizedUserName = registrationRequest.Email.ToUpper(),
                Email = registrationRequest.Email,
                NormalizedEmail = registrationRequest.Email.ToUpper(),
                PhoneNumber = registrationRequest.PhoneNUmber,
                FirstName = registrationRequest.FirstName,
                LastName = registrationRequest.LastName
            };

            try
            {
                var role = await _roleManager.FindByNameAsync(_customerRole);
                user.RoleId = role.Id;

                var result = await _userManager.CreateAsync(user, registrationRequest.Password);
                if (!result.Succeeded)
                {
                    throw new Exception(result.Errors.FirstOrDefault().Description);
                }

                var createUser = await _userRepository.GetByUserName(user.UserName, cancellationToken);
                await _userRepository.CreateUserBalance(createUser.Id, cancellationToken);
                
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task RegisterAdmin(RegistrationRequest registrationRequest, CancellationToken cancellationToken)
        {
            User user = new()
            {
                UserName = registrationRequest.Email,
                NormalizedUserName = registrationRequest.Email.ToUpper(),
                Email = registrationRequest.Email,
                NormalizedEmail = registrationRequest.Email.ToUpper(),
                PhoneNumber = registrationRequest.PhoneNUmber,
                FirstName = registrationRequest.FirstName,
                LastName = registrationRequest.LastName
            };

            try
            {
                var result = await _userManager.CreateAsync(user, registrationRequest.Password);
                if (result.Succeeded)
                {
                    var userToReturn = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == registrationRequest.Email.ToLower());
                    if (userToReturn != null)
                    {
                        if (!await _roleManager.RoleExistsAsync(_adminRole))
                        {
                            await _roleManager.CreateAsync(new Role(_adminRole));
                        }
                        await _userManager.AddToRoleAsync(userToReturn, _adminRole);
                    }
                }
                else
                {
                    throw new Exception(result.Errors.FirstOrDefault().Description);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }
}
