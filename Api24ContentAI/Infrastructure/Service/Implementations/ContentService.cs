using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ContentService : IContentService
    {
        private readonly IClaudeService _claudeService;
        private readonly ICustomTemplateService _customTemplateService;
        private readonly ITemplateService _templateService;
        private readonly IMarketplaceService _marketplaceService;
        private readonly IProductCategoryService _productCategoryService;
        private readonly IRequestLogService _requestLogService;

        public ContentService(IClaudeService claudeService,
                              ICustomTemplateService customTemplateService,
                              ITemplateService templateService,
                              IMarketplaceService marketplaceService,
                              IProductCategoryService productCategoryService,
                              IRequestLogService requestLogService)
        {
            _claudeService = claudeService;
            _customTemplateService = customTemplateService;
            _templateService = templateService;
            _marketplaceService = marketplaceService;
            _productCategoryService = productCategoryService;
            _requestLogService = requestLogService;
        }

        public async Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken)
        {
            var customTemplate = await _customTemplateService.GetByMarketplaceAndProductCategoryId(request.UniqueKey, request.ProductCategoryId, cancellationToken);

            var template = await _templateService.GetByProductCategoryId(request.ProductCategoryId, cancellationToken);

            var templateText = customTemplate == null ? template.Text : customTemplate.Text;

            var claudRequestContent = $"{request.ProductName} {templateText} {ConvertAttributes(request.Attributes)}";

            var claudeRequest = new ClaudeRequest(claudRequestContent);

            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = request
            }, cancellationToken);

            return new ContentAIResponse
            {
                Text = claudeResponse.Content.Single().Text
            };
        }

        private string ConvertAttributes(List<Domain.Models.Attribute> attributes)
        {
            var resultBuilder = new StringBuilder();
            foreach (var attribute in attributes)
            {
                resultBuilder.Append($"{attribute.Key}: {attribute.Value};");
            }
            return resultBuilder.ToString();
        }
    }
}

