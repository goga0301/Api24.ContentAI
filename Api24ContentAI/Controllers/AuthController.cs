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
    public class AuthController(IConfiguration configuration, IAuthService authService, IUserContentService userContentService, ILogger<AuthController> logger) : ControllerBase
    {
        private readonly IConfiguration _configuration = configuration;
        private readonly IAuthService _authService = authService;
        private readonly IUserContentService _userContentService = userContentService;
        private readonly ILogger<AuthController> _logger = logger;

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
                Expires = DateTime.UtcNow.AddMinutes(60),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("Security:SecretKey").Value)), SecurityAlgorithms.HmacSha256Signature),
            };

            JwtSecurityTokenHandler tokenHandler = new();
            SecurityToken securityToken = tokenHandler.CreateToken(tokenDescriptor);
            string token = tokenHandler.WriteToken(securityToken);

            return Ok(new { Token = token });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest loginRequestDTO, CancellationToken cancellationToken)
        {
            try
            {
                LoginResponse user = await _authService.Login(loginRequestDTO, cancellationToken);
                return Ok(user);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("login-with-fb")]
        public async Task<IActionResult> LoginWithFacebook([FromBody] string credential, CancellationToken cancellationToken)
        {
            try
            {
                LoginResponse user = await _authService.LoginWithFacebook(credential, cancellationToken);
                return Ok(user);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
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
            await _authService.RegisterWithPhone(registrationRequestDTO, cancellation);
            return Ok();
        }

        [HttpPost("login-with-phone")]
        public async Task<IActionResult> LoginWithPhone(LoginRequest loginRequest, CancellationToken cancellation)
        {
            try
            {
                LoginResponse loginResponse = await _authService.LoginWithPhone(loginRequest, cancellation);
                return Ok(loginResponse);
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }
        }
    }
}
