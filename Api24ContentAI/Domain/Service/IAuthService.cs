using Api24ContentAI.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IAuthService
    {
        Task Register(RegistrationRequest registrationRequest, CancellationToken cancellationToken, UserType userType = UserType.Normal);
        Task RegisterWithPhone(RegisterWIthPhoneRequest registrationRequestDto, CancellationToken cancellation, UserType userType = UserType.Normal);
        Task RegisterAdmin(RegistrationRequest registrationRequest, CancellationToken cancellationToken);
        Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken);
        Task<LoginResponse> LoginWithPhone(LoginWithPhoneRequest loginRequest, CancellationToken cancellationToken);
        
        Task<LoginResponse> LoginWithFacebook(string credentials, CancellationToken cancellationToken);
        Task<LoginResponse> RefreshToken(TokenModel tokenModel, CancellationToken cancellationToken);
    }
}
