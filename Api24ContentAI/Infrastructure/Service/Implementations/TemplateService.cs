using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Models.Mappers;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class TemplateService : ITemplateService
    {
        private readonly ITemplateRepository _templateRepository;

        public TemplateService(ITemplateRepository templateRepository)
        {
            _templateRepository = templateRepository;
        }

        public async Task<Guid> Create(CreateTemplateModel template, CancellationToken cancellationToken)
        {
           return await _templateRepository.Create(template.ToEntity(), cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _templateRepository.Delete(id, cancellationToken);
        }

        public async Task<List<TemplateModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _templateRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<TemplateModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return (await _templateRepository.GetById(id, cancellationToken)).ToModel();
        }

        public async Task<TemplateModel> GetByProductCategoryId(Guid productCategoryId, CancellationToken cancellationToken)
        {
            var template = await _templateRepository.GetByProductCategoryId(productCategoryId, cancellationToken);
            if (template == null) return null;
            return template.ToModel();
        }

        //public async Task<TemplateModel> GetByProductCategoryIdAndLanguage(Guid productCategoryId, string language, CancellationToken cancellationToken)
        //{
        //    var template = await _templateRepository.GetByProductCategoryIdAndLanguage(productCategoryId, language, cancellationToken);
        //    if (template == null) return null;
        //    return template.ToModel();
        //}

        public async Task Update(UpdateTemplateModel template, CancellationToken cancellationToken)
        {
            var entity = await _templateRepository.GetById(template.Id, cancellationToken);
            entity.Name = template.Name;
            entity.Text = template.Text;
            entity.ProductCategoryId = template.ProductCategoryId;

            await _templateRepository.Update(entity, cancellationToken);
        }
    }
}
