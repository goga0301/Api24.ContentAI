using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Models.Mappers;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Api24ContentAI.Domain.Service;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class LanguageService : ILanguageService
    {
        private readonly ILanguageRepository _languageRepository;

        public LanguageService(ILanguageRepository languageRepository)
        {
            _languageRepository = languageRepository;
        }

        public async Task Create(CreateLanguageModel language, CancellationToken cancellationToken)
        {
            await _languageRepository.Create(language.ToEntity(), cancellationToken);
        }

        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            await _languageRepository.Delete(id, cancellationToken);
        }

        public async Task<List<LanguageModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _languageRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<LanguageModel> GetById(int id, CancellationToken cancellationToken)
        {
            var language = await _languageRepository.GetById(id, cancellationToken);
            if (language == null)
            {
                throw new Exception("მითითებული ენა ვერ მოიძებნა");
            }
            return language.ToModel();
        }

        public async Task Update(UpdateLanguageModel language, CancellationToken cancellationToken)
        {
            var entity = await _languageRepository.GetById(language.Id, cancellationToken);
            entity.Name = language.Name;

            await _languageRepository.Update(entity, cancellationToken);
        }
    }
}
