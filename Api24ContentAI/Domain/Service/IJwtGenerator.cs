using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Security.Claims;

namespace Api24ContentAI.Domain.Service
{
    public interface IJwtGenerator
    {
        public string GenerateToken(IdentityUser applicationUser, IEnumerable<string> roles);
        public (string AccessToken, string RefreshToken) GenerateTokens(IdentityUser applicationUser, IEnumerable<string> roles);
        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token);


    }
}
