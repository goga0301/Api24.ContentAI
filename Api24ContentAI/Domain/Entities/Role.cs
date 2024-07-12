using Microsoft.AspNetCore.Identity;

namespace Api24ContentAI.Domain.Entities
{
    public class Role : IdentityRole<string>
    {
        public Role()
        {
        }

        public Role(string roleName) : base(roleName)
        {
        }
    }
}
