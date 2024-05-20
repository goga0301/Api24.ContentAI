using Api24ContentAI.Domain.Entities;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Domain.Repository
{
    public interface ILanguageRepository
    {
        IQueryable<Language> GetAll();

        Task<Language> GetById(int id, CancellationToken cancellationToken);

        Task Create(Language entity, CancellationToken cancellationToken);

        Task Update(Language entity, CancellationToken cancellationToken);

        Task Delete(int id, CancellationToken cancellationToken);
    }
}
