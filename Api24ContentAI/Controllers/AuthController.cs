using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Models;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IAuthService _authService;
        private readonly IUserContentService _userContentService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IConfiguration configuration, IAuthService authService, IUserContentService userContentService, ILogger<AuthController> logger)
        {
            _configuration = configuration;
            _authService = authService;
            _userContentService = userContentService;
            _logger = logger;
        }


        [HttpPost("basic")]
        [AllowAnonymous]
        public async Task<IActionResult> BasicMessage(BasicMessageRequest request, CancellationToken cancellationToken)
        {
            CopyrightAIResponse response = await _userContentService.BasicMessage(request, cancellationToken);
            return Ok(response);
        }

        [HttpPost("token")]
        [AllowAnonymous]
        public IActionResult GenerateToken(string username, string password)
        {
            if (!(username == _configuration.GetSection("Security:Username").Value &&
                password == _configuration.GetSection("Security:Password").Value))
            {
                return Unauthorized();
            }

            Claim[] claims = [new Claim(JwtRegisteredClaimNames.Sub, username)];

            SecurityTokenDescriptor tokenDescriptor = new()
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("Security:SecretKey").Value)), SecurityAlgorithms.HmacSha256Signature),
            };

            JwtSecurityTokenHandler tokenHandler = new();
            SecurityToken securityToken = tokenHandler.CreateToken(tokenDescriptor);
            string token = tokenHandler.WriteToken(securityToken);

            return Ok(new { Token = token });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] TokenModel tokenModel, CancellationToken cancellationToken)
        {
            try
            {
                LoginResponse user = await _authService.RefreshToken(tokenModel, cancellationToken);
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegistrationRequest registrationRequestDTO, CancellationToken cancellationToken)
        {
            await _authService.Register(registrationRequestDTO, cancellationToken);
            return Ok();
        }

        [HttpPost("register-with-phone")]
        public async Task<IActionResult> RegisterWithPhone(RegisterWIthPhoneRequest registrationRequestDTO, CancellationToken cancellation)
        {
            try
            {
                if (registrationRequestDTO == null)
                {
                    _logger.LogWarning("RegisterWithPhone called with null request");
                    return BadRequest(new { error = "Registration request is required" });
                }

                _logger.LogInformation("RegisterWithPhone called for phone: {PhoneNumber}", registrationRequestDTO.PhoneNumber);
                
                await _authService.RegisterWithPhone(registrationRequestDTO, cancellation);
                
                _logger.LogInformation("Successfully registered user with phone: {PhoneNumber}", registrationRequestDTO.PhoneNumber);
                return Ok(new { message = "User registered successfully" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input for RegisterWithPhone: {PhoneNumber}", registrationRequestDTO?.PhoneNumber);
                return BadRequest(new { error = "Invalid input", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during RegisterWithPhone for phone: {PhoneNumber}", registrationRequestDTO?.PhoneNumber);
                return BadRequest(new { error = "Registration failed", message = ex.Message });
            }
        }

        [HttpPost("login-with-phone")]
        public async Task<IActionResult> LoginWithPhone(LoginWithPhoneRequest loginRequest, CancellationToken cancellation)
        {
            try
            {
                if (loginRequest == null)
                {
                    _logger.LogWarning("LoginWithPhone called with null request");
                    return BadRequest(new { error = "Login request is required" });
                }

                _logger.LogInformation("LoginWithPhone called for phone: {PhoneNumber}", loginRequest.PhoneNumber);
                
                LoginResponse loginResponse = await _authService.LoginWithPhone(loginRequest, cancellation);
                
                _logger.LogInformation("Successfully logged in user with phone: {PhoneNumber}", loginRequest.PhoneNumber);
                return Ok(loginResponse);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input for LoginWithPhone: {PhoneNumber}", loginRequest?.PhoneNumber);
                return BadRequest(new { error = "Invalid input", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during LoginWithPhone for phone: {PhoneNumber}", loginRequest?.PhoneNumber);
                return BadRequest(new { error = "Login failed", message = ex.Message });
            }
        }

        [HttpPost("send-verification-code")]
        [AllowAnonymous]
        public async Task<IActionResult> SendVerificationCode(SendVerificationCodeRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var response =  await _authService.SendVerificationCode(request, cancellationToken);
                return Ok(response);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error sending verification code");
                return BadRequest(new { error = e.Message });
            }
        }
        
    }
}
