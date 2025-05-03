using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IApi24Service
    {
        Task<List<CategoryResponse>> GetCategories(CancellationToken cancellationToken);
    }
}
