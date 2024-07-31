using Api24ContentAI.Domain.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IAuthService
    {
        Task Register(RegistrationRequest registrationRequest, CancellationToken cancellationToken);
        Task RegisterAdmin(RegistrationRequest registrationRequest, CancellationToken cancellationToken);
        Task<LoginResponse> Login(LoginRequest loginRequest, CancellationToken cancellationToken);
        Task<LoginResponse> RefreshToken(TokenModel tokenModel, CancellationToken cancellationToken);
    }
}
