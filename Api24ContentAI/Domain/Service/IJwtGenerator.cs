using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Security.Claims;

namespace Api24ContentAI.Domain.Service
{
    public interface IJwtGenerator
    {
        string GenerateToken(IdentityUser applicationUser, IEnumerable<string> roles);
        (string AccessToken, string RefreshToken) GenerateTokens(IdentityUser applicationUser, IEnumerable<string> roles);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);


    }
}
