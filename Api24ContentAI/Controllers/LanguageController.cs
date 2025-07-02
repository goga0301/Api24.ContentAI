using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Authorization;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class LanguageController : Controller
    {
        private readonly ILanguageService _languageService;

        public LanguageController(ILanguageService languageService)
        {
            _languageService = languageService;
        }

        [HttpGet]
        public async Task<List<LanguageModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _languageService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<LanguageModel> GetById(int id, CancellationToken cancellationToken)
        {
            return await _languageService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateLanguageModel model, CancellationToken cancellationToken)
        {
            await _languageService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateLanguageModel model, CancellationToken cancellationToken)
        {
            await _languageService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            await _languageService.Delete(id, cancellationToken);
        }
    }
}
