using Microsoft.AspNetCore.Identity;

namespace Api24ContentAI.Domain.Entities
{
    public class User : IdentityUser
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string RoleId { get; set; }
        public UserBalance UserBalance { get; set; }
    }
}
