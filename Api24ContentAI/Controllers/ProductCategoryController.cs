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
    public class ProductCategoryController : ControllerBase
    {
        private readonly IProductCategoryService _productCategoryService;

        public ProductCategoryController(IProductCategoryService productCategoryService)
        {
            _productCategoryService = productCategoryService;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<List<ProductCategoryModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _productCategoryService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<ProductCategoryModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _productCategoryService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateProductCategoryModel model, CancellationToken cancellationToken)
        {
            await _productCategoryService.Create(model, cancellationToken);
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateProductCategoryModel model, CancellationToken cancellationToken)
        {
            await _productCategoryService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _productCategoryService.Delete(id, cancellationToken);
        }

        [HttpGet("sync")]
        public async Task SyncCategories(CancellationToken cancellationToken)
        {
            await _productCategoryService.SyncCategories(cancellationToken);
        }
    }
}
