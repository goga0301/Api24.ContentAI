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
        
        public string FirstName { get; set; }
        
        public string LastName { get; set; }
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
    
    public class SendVerificationCodeRequest
    {
        public string PhoneNumber { get; set; }
    }

    public class VerificationCodeResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public int Code { get; set; }
    }

    public class SmsApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public object Output { get; set; }
        public int ErrorCode { get; set; }
    }
    
}
