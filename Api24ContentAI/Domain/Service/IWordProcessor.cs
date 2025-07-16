using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Service;

public interface IWordProcessor : IFileProcessor
{
    Task<int> CountPagesAsync(IFormFile file, CancellationToken cancellationToken);
}