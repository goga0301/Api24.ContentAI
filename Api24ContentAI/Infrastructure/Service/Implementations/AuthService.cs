using Api24ContentAI.Domain.Entities;
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
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.Extensions.Options;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly ContentDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly IJwtGenerator _jwtTokenGenerator;
        private readonly IUserRepository _userRepository;
        private readonly HttpClient _httpClient;
        private readonly FbOptions _fbOptions;
        private const string _adminRole = "administrator";
        private const string _customerRole = "user";


        public AuthService(ContentDbContext context,
                           UserManager<User> userManager,
                           RoleManager<Role> roleManager,
                           IUserRepository userRepository,
                           IJwtGenerator jwtTokenGenerator,
                           HttpClient httpClient, IOptions<FbOptions> fbOptions)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtTokenGenerator = jwtTokenGenerator;
            _userRepository = userRepository;
            _httpClient = httpClient;
            _fbOptions = fbOptions.Value;
        }

        public async Task Register(RegistrationRequest registrationRequest, CancellationToken cancellationToken, UserType userType = UserType.Normal)
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
                LastName = registrationRequest.LastName,
                UserType = userType
            };

            try
            {
                await EnsureRolesExistsAsync(_customerRole);

                Role role = await _roleManager.FindByNameAsync(_customerRole);

                user.RoleId = role.Id;

                IdentityResult result = await _userManager.CreateAsync(user, registrationRequest.Password);
                if (!result.Succeeded)
                {
                    throw new Exception(result.Errors.FirstOrDefault().Description);
                }

                User createUser = await _userRepository.GetByUserName(user.UserName, cancellationToken);
                await _userRepository.CreateUserBalance(createUser.Id, cancellationToken);

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        

        public async Task RegisterWithPhone(RegisterWIthPhoneRequest request, CancellationToken cancellationToken, UserType userType = UserType.Normal)
        {
            string uniqueEmail = $"phone_{request.PhoneNumber}_{Guid.NewGuid()}@example.com"; // temporary
            
            User user = new()
            {
                UserName = request.PhoneNumber, // this is required by dotnet
                FirstName = request.PhoneNumber, // temporary
                LastName = request.PhoneNumber, // temporary
                NormalizedUserName = request.PhoneNumber.ToUpper(),
                Email = uniqueEmail,
                NormalizedEmail = uniqueEmail.ToUpper(),
                PhoneNumber = request.PhoneNumber,
                EmailConfirmed = true,
                PhoneNumberConfirmed = true,
                UserType = userType,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            try
            {
                await EnsureRolesExistsAsync(_customerRole);

                Role role = await _roleManager.FindByNameAsync(_customerRole);
                user.RoleId = role.Id;

                IdentityResult result = await _userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    string errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    throw new Exception($"Failed to create user: {errors}");
                }

                User createdUser = await _userRepository.GetByUserName(user.UserName, cancellationToken);
                await _userRepository.CreateUserBalance(createdUser.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                Exception innerException = ex.InnerException;
                string innerExceptionMessage = innerException != null ? 
                    $" Inner exception: {innerException.Message}" : "";
                
                throw new Exception($"Registration failed: {ex.Message}{innerExceptionMessage}", ex);
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
                IdentityResult result = await _userManager.CreateAsync(user, registrationRequest.Password);
                if (result.Succeeded)
                {
                    User userToReturn = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == registrationRequest.Email.ToLower());
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

        public async Task<LoginResponse> LoginWithPhone(LoginWithPhoneRequest loginRequest, CancellationToken cancellationToken)
        {
            User user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == loginRequest.PhoneNumber, cancellationToken: cancellationToken);;
            if (user == null)
            {
                throw new Exception($"User not found by phone number {loginRequest.PhoneNumber}");
            }
            var isValid = await _userManager.CheckPasswordAsync(user, loginRequest.Password);
            if (!isValid)
            {
                throw new Exception("Password is not correct");
            }
            var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId, cancellationToken: cancellationToken);
            var (accessToken, refreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _userManager.UpdateAsync(user);
            return new LoginResponse()
            {
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        public async Task<LoginResponse> LoginWithFacebook(string credentials, CancellationToken cancellationToken)
        {
            string applicationId = _fbOptions.ApplicationId;
            string secret = _fbOptions.Secret;
            HttpResponseMessage response = await _httpClient.GetAsync($"https://graph.facebook.com/debug?input_token={credentials}&access_token={applicationId}|{secret}", cancellationToken);
            string stringThing = await response.Content.ReadAsStringAsync(cancellationToken);
            FBUser userObject = JsonConvert.DeserializeObject<FBUser>(stringThing);
            //var user = await _authService.Login(loginRequestDTO, cancellationToken);

            if (!userObject.Data.IsValid)
            {
                return null;
            }

            HttpResponseMessage infoResponse = await _httpClient.GetAsync($"https://graph.facebook.com/me?fields=first_name,last_name,email,id&access_token={applicationId}|{secret}", cancellationToken);
            string userInfoContent = await infoResponse.Content.ReadAsStringAsync(cancellationToken);
            FBUserInfo userInfo = JsonConvert.DeserializeObject<FBUserInfo>(userInfoContent);

            User user = await _userManager.FindByEmailAsync(userInfo.Email);
            if (user == null)
            {
                RegistrationRequest registration = new RegistrationRequest()
                {
                    FirstName = userInfo.FirstName,
                    LastName = userInfo.LastName,
                    Email = userInfo.Email,
                    Password = userInfo.Id,
                    PhoneNUmber = ""
                };

                await Register(registration, cancellationToken, UserType.Facebook);
                user = await _userManager.FindByEmailAsync(userInfo.Email);
            }

            Role role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            (string accessToken, string refreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await _userManager.UpdateAsync(user);

            return new LoginResponse()
            {
                Token = accessToken,
                RefreshToken = refreshToken
            };

        }


        public async Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken)
        {
            User user = await _userManager.FindByNameAsync(loginRequest.UserName);
            if (user == null)
            {
                throw new Exception($"User not found by username {loginRequest.UserName}");
            }
            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequest.Password);

            if (!isValid)
            {
                throw new Exception("Password is not correct");
            }

            Role role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            (string accessToken, string refreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await _userManager.UpdateAsync(user);

            return new LoginResponse()
            {
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        public async Task<LoginResponse> RefreshToken(TokenModel tokenModel, CancellationToken cancellationToken)
        {
            if (tokenModel is null || string.IsNullOrEmpty(tokenModel.RefreshToken))
            {
                throw new Exception("Invalid client request: refresh token is required");
            }

            string refreshToken = tokenModel.RefreshToken;

            User user = await _context.Users
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken && u.RefreshTokenExpiryTime > DateTime.UtcNow, 
                    cancellationToken);

            if (user == null)
            {
                throw new Exception("Invalid or expired refresh token");
            }

            Role role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId, cancellationToken);
            if (role == null)
            {
                throw new Exception($"Role not found for user: {user.UserName}");
            }

            (string newAccessToken, string newRefreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string> { role.NormalizedName });

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await _userManager.UpdateAsync(user);

            return new LoginResponse { Token = newAccessToken, RefreshToken = newRefreshToken };
        }
        

        private async Task EnsureRolesExistsAsync(string roleName)
        {
            Role role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                role = new Role(roleName);
                IdentityResult roleResult = await _roleManager.CreateAsync(role);
                if (!roleResult.Succeeded)
                {
                    throw new Exception($"Failed to create role '{roleName}': {roleResult.Errors.FirstOrDefault()?.Description}");
                }
            }
        }
    }
}
