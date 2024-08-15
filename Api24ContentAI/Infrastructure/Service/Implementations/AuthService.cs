using Api24ContentAI.Domain.Entities;
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

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly ContentDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly IJwtGenerator _jwtTokenGenerator;
        private readonly IUserRepository _userRepository;
        private readonly IEmailSenderService _emailSenderService;
        private const string _adminRole = "administrator";
        private const string _customerRole = "user";
        private const string emailVerificationSubject = "Verification Code";


        public AuthService(ContentDbContext context,
                           UserManager<User> userManager,
                           RoleManager<Role> roleManager,
                           IUserRepository userRepository,
                           IJwtGenerator jwtTokenGenerator,
                           IEmailSenderService emailSenderService)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtTokenGenerator = jwtTokenGenerator;
            _userRepository = userRepository;
            _emailSenderService = emailSenderService;   
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

                var code = GenerateRandomCode();
                user.EmailAuthorizationCode = code;
                await _emailSenderService.SendEmailAsync(user.Email, EmailVerificationBody(code), emailVerificationSubject, cancellationToken);
                await _userManager.UpdateAsync(user);

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        private string GenerateRandomCode()
        {
            Random random = new Random();
            int code = random.Next(100000, 999999);
            return code.ToString();
        }
        private string EmailVerificationBody(string code) => $"<b>This is your verification code: {code}</b>";

        public async Task VerifyEmail(string email, string userInput, CancellationToken cancellationToken)
        {
            var user = await _userManager.FindByEmailAsync(email) ?? throw new Exception("User not found");

            if ( user.EmailAuthorizationCode != userInput)
            {
                throw new Exception("code is incorrect");
            }

            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
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

        public async Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken)
        {
            var user = await _userManager.FindByNameAsync(loginRequest.UserName);
            if (user == null)
            {
                throw new Exception($"User not found by username {loginRequest.UserName}");
            }
            bool isValid = await _userManager.CheckPasswordAsync(user, loginRequest.Password);

            if (!isValid)
            {
                throw new Exception("Password is not correct");
            }

            var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            var (accessToken, refreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string>() { role.NormalizedName });

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
            if (tokenModel is null)
            {
                throw new Exception("Invalid client request");
            }

            string accessToken = tokenModel.AccessToken;
            string refreshToken = tokenModel.RefreshToken;

            var principal = _jwtTokenGenerator.GetPrincipalFromExpiredToken(accessToken);
            if (principal == null)
            {
                throw new Exception("Invalid access token or refresh token");
            }

            string username = principal.Identity.Name;
            var user = await _userManager.FindByNameAsync(username);

            if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                throw new Exception("Invalid access token or refresh token");
            }

            var role = await _context.Roles.SingleOrDefaultAsync(x => x.Id == user.RoleId);
            var (newAccessToken, newRefreshToken) = _jwtTokenGenerator.GenerateTokens(user, new List<string> { role.NormalizedName});

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
            await _userManager.UpdateAsync(user);

            return new LoginResponse { Token = newAccessToken, RefreshToken = newRefreshToken };
        }
    }
}
