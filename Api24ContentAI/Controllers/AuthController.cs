﻿using Microsoft.AspNetCore.Authorization;
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

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IAuthService _authService;

        public AuthController(IConfiguration configuration, IAuthService authService)
        {
            _configuration = configuration;
            _authService = authService;
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

            var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, username) };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(60),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("Security:SecretKey").Value)), SecurityAlgorithms.HmacSha256Signature),
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var securityToken = tokenHandler.CreateToken(tokenDescriptor);
            var token = tokenHandler.WriteToken(securityToken);

            return Ok(new { Token = token });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest loginRequestDTO, CancellationToken cancellationToken)
        {
            try
            {
                var user = await _authService.Login(loginRequestDTO, cancellationToken);
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
                var user = await _authService.RefreshToken(tokenModel, cancellationToken);
                return Ok(user);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegistrationRequest registrationRequestDTO, CancellationToken cancellationToken)
        {
            await _authService.Register(registrationRequestDTO, cancellationToken);
            return Ok();
        }

        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail(string email, string userInput, CancellationToken cancellationToken)
        {
            await _authService.VerifyEmail(email, userInput, cancellationToken);
            return Ok();
        }
    }
}
