using Api24ContentAI.Domain.Models;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IAuthService
    {
        Task Register(RegistrationRequest registrationRequest);
        Task RegisterAdmin(RegistrationRequest registrationRequest);
        Task<LoginResponse> Login(LoginRequest loginRequest);
    }
}
