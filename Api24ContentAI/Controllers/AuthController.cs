using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System;
using Microsoft.Extensions.Configuration;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
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
    }
}
