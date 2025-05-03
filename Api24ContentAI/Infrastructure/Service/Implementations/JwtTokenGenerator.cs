using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Identity;
using System;
using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class JwtTokenGenerator : IJwtGenerator
    {
        private readonly JwtOptions _jwtOptions;
        public JwtTokenGenerator(IOptions<JwtOptions> jwtOptions)
        {
            _jwtOptions = jwtOptions.Value;
        }
        public string GenerateToken(IdentityUser applicationUser, IEnumerable<string> roles)
        {
            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(_jwtOptions.Secret);
            List<Claim> claimList = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, applicationUser.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, applicationUser.UserName),
                new Claim(JwtRegisteredClaimNames.Email, applicationUser.Email),
                new Claim("UserId",applicationUser.Id),
            };
            claimList.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor()
            {
                Audience = _jwtOptions.Audience,
                Issuer = _jwtOptions.Issuer,
                Subject = new ClaimsIdentity(claimList),
                Expires = DateTime.UtcNow.AddHours(1),

                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature)
            };

            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public (string AccessToken, string RefreshToken) GenerateTokens(IdentityUser applicationUser, IEnumerable<string> roles)
        {
            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(_jwtOptions.Secret);
            List<Claim> claimList = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, applicationUser.Id),
        new Claim(JwtRegisteredClaimNames.UniqueName, applicationUser.UserName),
        new Claim(JwtRegisteredClaimNames.Email, applicationUser.Email),
        new Claim("UserId", applicationUser.Id),
    };
            claimList.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor()
            {
                Audience = _jwtOptions.Audience,
                Issuer = _jwtOptions.Issuer,
                Subject = new ClaimsIdentity(claimList),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature)
            };
            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
            string accessToken = tokenHandler.WriteToken(token);
            string refreshToken = GenerateRefreshToken();

            return (accessToken, refreshToken);
        }
        public (string AccessToken, string RefreshToken) RefreshToken(string accessToken, string refreshToken)
        {
            TokenValidationParameters tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_jwtOptions.Secret)),
                ValidateLifetime = false
            };

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            ClaimsPrincipal principal = tokenHandler.ValidateToken(accessToken, tokenValidationParameters, out securityToken);
            JwtSecurityToken jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            // Here you should validate the refresh token against the stored one in your database
            // For this example, we'll assume it's valid

            (string AccessToken, string RefreshToken) newAccessToken = GenerateTokens(new IdentityUser
            {
                Id = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                UserName = principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value,
                Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            }, principal.FindAll(ClaimTypes.Role).Select(c => c.Value));

            return newAccessToken;
        }
        public string GenerateRefreshToken()
        {
            byte[] randomNumber = new byte[512];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }


        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            TokenValidationParameters tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret)),
                ValidateLifetime = false
            };

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken securityToken;
            ClaimsPrincipal principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
            JwtSecurityToken jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }
    }
}
