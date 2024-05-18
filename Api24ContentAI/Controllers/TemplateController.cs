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
    public class TemplateController : ControllerBase
    {
        private readonly ITemplateService _templateService;

        public TemplateController(ITemplateService templateService)
        {
            _templateService = templateService;
        }

        [HttpGet]
        public async Task<List<TemplateModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _templateService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<TemplateModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _templateService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task<Guid> Create([FromBody] CreateTemplateModel model, CancellationToken cancellationToken)
        {
            return await _templateService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateTemplateModel model, CancellationToken cancellationToken)
        {
            await _templateService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _templateService.Delete(id, cancellationToken);
        }
    }
}
