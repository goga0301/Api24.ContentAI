using Api24ContentAI.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Repository
{
    public interface ITemplateRepository : IGenericRepository<Template>
    {
        Task<Template> GetByProductCategoryId(Guid productCategoryId, CancellationToken cancellationToken);
        Task<Template> GetByProductCategoryIdAndLanguage(Guid productCategoryId, string language, CancellationToken cancellationToken);
    }
}
