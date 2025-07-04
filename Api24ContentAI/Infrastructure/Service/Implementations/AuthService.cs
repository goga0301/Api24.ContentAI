﻿using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Service;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly ContentDbContext context;
        private readonly UserManager<User> userManager;
        private readonly RoleManager<Role> roleManager;
        private readonly IUserRepository userRepository;
        private readonly IJwtGenerator jwtTokenGenerator;
        private readonly HttpClient httpClient;
        private readonly FbOptions _fbOptions;
        private readonly IConfiguration _configuration;
        private const string _adminRole = "administrator";
        private const string _customerRole = "user";

        public AuthService(
            ContentDbContext context,
            UserManager<User> userManager,
            RoleManager<Role> roleManager,
            IUserRepository userRepository,
            IJwtGenerator jwtTokenGenerator,
            HttpClient httpClient,
            IOptions<FbOptions> fbOptions,
            IConfiguration configuration)
        {
            this.context = context;
            this.userManager = userManager;
            this.roleManager = roleManager;
            this.userRepository = userRepository;
            this.jwtTokenGenerator = jwtTokenGenerator;
            this.httpClient = httpClient;
            this._fbOptions = fbOptions.Value;
            this._configuration = configuration;
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

                Role role = await roleManager.FindByNameAsync(_customerRole);

                user.RoleId = role.Id;

                IdentityResult result = await userManager.CreateAsync(user, registrationRequest.Password);
                if (!result.Succeeded)
                {
                    throw new Exception(result.Errors.FirstOrDefault().Description);
                }

                User createUser = await userRepository.GetByUserName(user.UserName, cancellationToken);
                await userRepository.CreateUserBalance(createUser.Id, cancellationToken);

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
        

        public async Task RegisterWithPhone(RegisterWIthPhoneRequest request, CancellationToken cancellationToken, UserType userType = UserType.Normal)
        {
            // Input validation
            if (request == null)
            {
                throw new ArgumentException("Registration request cannot be null");
            }
            
            if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                throw new ArgumentException("Phone number is required");
            }
            
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ArgumentException("Password is required");
            }

            // Check if user already exists with this phone number
            var existingUser = await userManager.Users.FirstOrDefaultAsync(u => u.UserName == request.PhoneNumber, cancellationToken);
            if (existingUser != null)
            {
                throw new Exception($"User with phone number {request.PhoneNumber} already exists");
            }

            string uniqueEmail = $"phone_{request.PhoneNumber}_{Guid.NewGuid()}@example.com";
            
            User user = new()
            {
                UserName = request.PhoneNumber,
                FirstName = request.FirstName ?? "Unknown",
                LastName = request.LastName ?? "Unknown",
                NormalizedUserName = request.PhoneNumber.ToUpper(),
                Email = uniqueEmail,
                NormalizedEmail = uniqueEmail.ToUpper(),
                PhoneNumber = request.PhoneNumber,
                EmailConfirmed = true,
                PhoneNumberConfirmed = true,
                UserType = userType,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await EnsureRolesExistsAsync(_customerRole);

                Role role = await roleManager.FindByNameAsync(_customerRole);
                if (role == null)
                {
                    throw new Exception($"Role '{_customerRole}' not found after creation attempt");
                }
                
                user.RoleId = role.Id;

                IdentityResult result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    string errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    throw new Exception($"Failed to create user: {errors}");
                }

                User createdUser = await userRepository.GetByUserName(user.UserName, cancellationToken);
                if (createdUser == null)
                {
                    throw new Exception("Failed to retrieve created user");
                }
                
                await userRepository.CreateUserBalance(createdUser.Id, cancellationToken);
                
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
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
                IdentityResult result = await userManager.CreateAsync(user, registrationRequest.Password);
                if (result.Succeeded)
                {
                    User userToReturn = await context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == registrationRequest.Email.ToLower());
                    if (userToReturn != null)
                    {
                        if (!await roleManager.RoleExistsAsync(_adminRole))
                        {
                            await roleManager.CreateAsync(new Role(_adminRole));
                        }
                        await userManager.AddToRoleAsync(userToReturn, _adminRole);
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
            // Input validation
            if (loginRequest == null)
            {
                throw new ArgumentException("Login request cannot be null");
            }
            
            if (string.IsNullOrWhiteSpace(loginRequest.PhoneNumber))
            {
                throw new ArgumentException("Phone number is required");
            }
            
            if (string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                throw new ArgumentException("Password is required");
            }

            User user = await userManager.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserName == loginRequest.PhoneNumber, cancellationToken);
                
            if (user == null)
            {
                throw new Exception($"User not found with phone number {loginRequest.PhoneNumber}");
            }
            
            var isValid = await userManager.CheckPasswordAsync(user, loginRequest.Password);
            if (!isValid)
            {
                throw new Exception("Invalid password");
            }

            if (user.Role == null)
            {
                var role = await context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId, cancellationToken);
                if (role == null)
                {
                    throw new Exception($"Role not found for user {user.UserName}");
                }
                user.Role = role;
            }

            var (accessToken, refreshToken) = jwtTokenGenerator.GenerateTokens(user, new List<string> { user.Role.NormalizedName });
            
            // Update refresh token with retry logic
            int retryCount = 0;
            const int maxRetries = 3;
            
            while (retryCount < maxRetries)
            {
                try
                {
                    user.RefreshToken = refreshToken;
                    user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
                    await userManager.UpdateAsync(user);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (retryCount < maxRetries - 1)
                {
                    retryCount++;
                    await Task.Delay(100 * retryCount, cancellationToken); // Exponential backoff
                    
                    // Refresh user from database
                    user = await userManager.Users
                        .Include(u => u.Role)
                        .FirstOrDefaultAsync(u => u.UserName == loginRequest.PhoneNumber, cancellationToken);
                        
                    if (user == null)
                    {
                        throw new Exception("User not found during retry");
                    }
                }
            }
            
            return new LoginResponse
            {
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        public async Task<LoginResponse> LoginWithFacebook(string credentials, CancellationToken cancellationToken)
        {
            string applicationId = _fbOptions.ApplicationId;
            string secret = _fbOptions.Secret;
            HttpResponseMessage response = await httpClient.GetAsync($"https://graph.facebook.com/debug?input_token={credentials}&access_token={applicationId}|{secret}", cancellationToken);
            string stringThing = await response.Content.ReadAsStringAsync(cancellationToken);
            FBUser userObject = JsonConvert.DeserializeObject<FBUser>(stringThing);
            //var user = await _authService.Login(loginRequestDTO, cancellationToken);

            if (!userObject.Data.IsValid)
            {
                return null;
            }

            HttpResponseMessage infoResponse = await httpClient.GetAsync($"https://graph.facebook.com/me?fields=first_name,last_name,email,id&access_token={applicationId}|{secret}", cancellationToken);
            string userInfoContent = await infoResponse.Content.ReadAsStringAsync(cancellationToken);
            FBUserInfo userInfo = JsonConvert.DeserializeObject<FBUserInfo>(userInfoContent);

            User user = await userManager.FindByEmailAsync(userInfo.Email);
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
                user = await userManager.FindByEmailAsync(userInfo.Email);
            }

            Role role = await context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            (string accessToken, string refreshToken) = jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await userManager.UpdateAsync(user);

            return new LoginResponse()
            {
                Token = accessToken,
                RefreshToken = refreshToken
            };

        }


        public async Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken)
        {
            User user = await userManager.FindByNameAsync(loginRequest.UserName);
            if (user == null)
            {
                throw new Exception($"User not found by username {loginRequest.UserName}");
            }
            bool isValid = await userManager.CheckPasswordAsync(user, loginRequest.Password);

            if (!isValid)
            {
                throw new Exception("Password is not correct");
            }

            Role role = await context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            (string accessToken, string refreshToken) = jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await userManager.UpdateAsync(user);

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

            User user = await context.Users
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken && u.RefreshTokenExpiryTime > DateTime.UtcNow, 
                    cancellationToken);

            if (user == null)
            {
                throw new Exception("Invalid or expired refresh token");
            }

            Role role = await context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId, cancellationToken);
            if (role == null)
            {
                throw new Exception($"Role not found for user: {user.UserName}");
            }

            (string newAccessToken, string newRefreshToken) = jwtTokenGenerator.GenerateTokens(user, new List<string> { role.NormalizedName });

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await userManager.UpdateAsync(user);

            return new LoginResponse { Token = newAccessToken, RefreshToken = newRefreshToken };
        }

        public async Task<VerificationCodeResult> SendVerificationCode(SendVerificationCodeRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return new VerificationCodeResult
                    {
                        IsSuccess = false,
                        Message = "Phone number is required",
                        Code = -1
                    };
                }
                
                var random = new Random();
                var code = random.Next(100000, 999999);
                

                var smsApiKey = _configuration.GetSection("Security:SMS_API_KEY").Value ?? string.Empty ;
                if (string.IsNullOrEmpty(smsApiKey))
                {
                    return new VerificationCodeResult
                    {
                        IsSuccess = false,
                        Message = "SMS service configuration error",
                        Code = -1
                    };
                }
                
                var smsContent = $"Your Verification Code is: {code}";
                
                var formData = new List<KeyValuePair<string, string>>
                {
                    new("key", smsApiKey),
                    new("destination", request.PhoneNumber),
                    new("sender", "SULIKO.GE"),
                    new("content", smsContent)
                };
                
                var formContent = new FormUrlEncodedContent(formData);
                
                var response = await httpClient.PostAsync("https://smsoffice.ge/api/v2/send/", formContent, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                var apiResponse = JsonConvert.DeserializeObject<SmsApiResponse>(responseContent);

                if (apiResponse.Success)
                {
                    return new VerificationCodeResult
                    {
                        IsSuccess = apiResponse.Success,
                        Message = apiResponse.Message,
                        Code = code
                    };
                }

                var errorMessage = apiResponse?.Message ?? "Unknown error occurred";
                return new VerificationCodeResult
                {
                    IsSuccess = false,
                    Message = $"Failed to send SMS: {errorMessage}",
                    Code = -1
                };
            }
            catch (JsonException jsonEx)
            {
                return new VerificationCodeResult
                {
                    IsSuccess = false,
                    Message = $"Error parsing SMS API response: {jsonEx.Message}",
                    Code = -1
                };
            }
            catch (Exception ex)
            {
                return new VerificationCodeResult
                {
                    IsSuccess = false,
                    Message = $"Error sending verification code: {ex.Message}",
                    Code = -1
                };
            }
        }


        private async Task EnsureRolesExistsAsync(string roleName)
        {
            Role role = await roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                try
                {
                    role = new Role(roleName);
                    IdentityResult roleResult = await roleManager.CreateAsync(role);
                    if (!roleResult.Succeeded)
                    {
                        var existingRole = await roleManager.FindByNameAsync(roleName);
                        if (existingRole == null)
                        {
                            string errors = string.Join(", ", roleResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                            throw new Exception($"Failed to create role '{roleName}': {errors}");
                        }
                        // Role exists now, so another thread created it - this is fine
                    }
                }
                catch (InvalidOperationException)
                {
                    // This can happen if another thread creates the role at the same time
                    // Double-check that the role now exists
                    role = await roleManager.FindByNameAsync(roleName);
                    if (role == null)
                    {
                        throw new Exception($"Failed to create or find role '{roleName}' after retry");
                    }
                }
            }
        }
    }
}
