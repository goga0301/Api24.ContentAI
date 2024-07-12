using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace Api24ContentAI.Domain.Service
{
    public interface IJwtGenerator
    {
        string GenerateToken(IdentityUser applicationUser, IEnumerable<string> roles);

    }
}
