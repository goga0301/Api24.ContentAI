using Microsoft.AspNetCore.Identity;
using System;

namespace Api24ContentAI.Domain.Entities
{
    public class User : IdentityUser
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string RoleId { get; set; }
        public string EmailAuthorizationCode { get; set; }
        public string RefreshToken { get; set; }
        public DateTime RefreshTokenExpiryTime { get; set; }
        public Role Role { get; set; }
        public UserBalance UserBalance { get; set; }
    }
}
