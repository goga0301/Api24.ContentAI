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
        private readonly IRequestLogService _requestLogService;
        private const string DefaultTemplate = "You can use these template in order to understand the structure of the response. Give creative response containing the product consistency, how to use, brand information, recommendations and other information. Output should be in paragraphs and in Georgian. Output HTML Language (Small Bold headers, Bullet points, paragraphs, various tags and etc)";
        public ContentService(IClaudeService claudeService,
                              ICustomTemplateService customTemplateService,
                              ITemplateService templateService,
                              IRequestLogService requestLogService)
        {
            _claudeService = claudeService;
            _customTemplateService = customTemplateService;
            _templateService = templateService;
            _requestLogService = requestLogService;
        }

        public async Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken)
        {
            var templateText = DefaultTemplate;

            var template = await _templateService.GetByProductCategoryId(request.ProductCategoryId, cancellationToken);

            if (template != null)
            {
                templateText = template.Text;
            }

            var customTemplate = await _customTemplateService.GetByMarketplaceAndProductCategoryId(request.UniqueKey, request.ProductCategoryId, cancellationToken);

            if (customTemplate != null)
            {
                templateText = customTemplate.Text;
            }

            var claudRequestContent = $"{request.ProductName} {templateText} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

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
                resultBuilder.Append($"{attribute.Key}: {attribute.Value}; \n");
            }
            return resultBuilder.ToString();
        }
    }
}

