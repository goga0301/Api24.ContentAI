using Api24ContentAI.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface ITemplateService
    {
        Task<List<TemplateModel>> GetAll(CancellationToken cancellationToken);
        Task<TemplateModel> GetById(Guid id, CancellationToken cancellationToken);
        Task<Guid> Create(CreateTemplateModel template, CancellationToken cancellationToken);
        Task Update(UpdateTemplateModel template, CancellationToken cancellationToken);
        Task Delete(Guid id, CancellationToken cancellationToken);
        Task<TemplateModel> GetByProductCategoryId(Guid productCategoryId, CancellationToken cancellationToken);
        //Task<TemplateModel> GetByProductCategoryIdAndLanguage(Guid productCategoryId, string language, CancellationToken cancellationToken);
    }
}
