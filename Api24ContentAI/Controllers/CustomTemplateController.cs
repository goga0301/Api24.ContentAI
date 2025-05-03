using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Threading;
using Api24ContentAI.Domain.Service;
using System.Collections.Generic;
using Api24ContentAI.Domain.Models;
using System;
using Microsoft.AspNetCore.Authorization;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class CustomTemplateController(ICustomTemplateService customTemplateService) : ControllerBase
    {
        private readonly ICustomTemplateService _customTemplateService = customTemplateService;

        [HttpGet]
        public async Task<List<CustomTemplateModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _customTemplateService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<CustomTemplateModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _customTemplateService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task<Guid> Create([FromBody] CreateCustomTemplateModel model, CancellationToken cancellationToken)
        {
            return await _customTemplateService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateCustomTemplateModel model, CancellationToken cancellationToken)
        {
            await _customTemplateService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _customTemplateService.Delete(id, cancellationToken);
        }
    }
}
