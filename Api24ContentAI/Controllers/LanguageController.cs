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
    public class LanguageController(ILanguageService LanguageService) : Controller
    {
        private readonly ILanguageService _LanguageService = LanguageService;

        [HttpGet]
        public async Task<List<LanguageModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _LanguageService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<LanguageModel> GetById(int id, CancellationToken cancellationToken)
        {
            return await _LanguageService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateLanguageModel model, CancellationToken cancellationToken)
        {
            await _LanguageService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateLanguageModel model, CancellationToken cancellationToken)
        {
            await _LanguageService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            await _LanguageService.Delete(id, cancellationToken);
        }
    }
}
