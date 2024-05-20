using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Service
{
    public interface ILanguageService
    {
        Task<List<LanguageModel>> GetAll(CancellationToken cancellationToken);
        Task<LanguageModel> GetById(int id, CancellationToken cancellationToken);
        Task Create(CreateLanguageModel Language, CancellationToken cancellationToken);
        Task Update(UpdateLanguageModel Language, CancellationToken cancellationToken);
        Task Delete(int id, CancellationToken cancellationToken);
    }
}
