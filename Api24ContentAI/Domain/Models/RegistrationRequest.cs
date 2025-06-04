namespace Api24ContentAI.Domain.Models
{
    public class RegistrationRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNUmber { get; set; }
        public string Password { get; set; }
    }

    public class RegisterWIthPhoneRequest
    {
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
    }

    public class LoginRequest
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public class LoginWithPhoneRequest
    {
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
    }

    public class TokenModel
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
    
}
