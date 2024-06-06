using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        private readonly IProductCategoryService _productCategoryService;
        private readonly IMarketplaceService _marketplaceService;
        private readonly ILanguageService _languageService;

        public ContentService(IClaudeService claudeService,
                              ICustomTemplateService customTemplateService,
                              ITemplateService templateService,
                              IRequestLogService requestLogService,
                              IProductCategoryService productCategoryService,
                              IMarketplaceService marketplaceService,
                              ILanguageService languageService)
        {
            _claudeService = claudeService;
            _customTemplateService = customTemplateService;
            _templateService = templateService;
            _requestLogService = requestLogService;
            _productCategoryService = productCategoryService;
            _marketplaceService = marketplaceService;
            _languageService = languageService;
        }

        public async Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken)
        {
            var marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);
            if (marketplace != null && marketplace.ContentLimit <= 0)
            {
                throw new Exception("ContentAI რექვესთების ბალანსი ამოიწურა");
            }
            var productCategory = await _productCategoryService.GetById(request.ProductCategoryId, cancellationToken);

            var templateText = GetDefaultTemplate(productCategory.NameEng);

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

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);

            var claudRequestContent = $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

            var claudeRequest = new ClaudeRequest(claudRequestContent);

            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request),
                RequestType = RequestType.Content

            }, cancellationToken);

            await _marketplaceService.Update(new UpdateMarketplaceModel
            {
                Id = request.UniqueKey,
                Name = marketplace.Name,
                TranslateLimit = marketplace.TranslateLimit,
                ContentLimit = marketplace.ContentLimit - 1,
            }, cancellationToken);

            return new ContentAIResponse
            {
                Text = claudeResponse.Content.Single().Text.Replace("\n", "<br>")
            };
        }

        public async Task<TranslateResponse> Translate(TranslateRequest request, CancellationToken cancellationToken)
        {
            var marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace != null && marketplace.TranslateLimit <= 0)
            {
                throw new Exception("Translate რექვესთების ბალანსი ამოიწურა");
            }

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);

            var templateText = GetTranslateTemplate(language.Name, request.Description);

            var claudeRequest = new ClaudeRequest(templateText);
            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request),
                RequestType = RequestType.Translate
            }, cancellationToken);

            await _marketplaceService.Update(new UpdateMarketplaceModel
            {
                Id = request.UniqueKey,
                Name = marketplace.Name,
                TranslateLimit = marketplace.TranslateLimit - 1,
                ContentLimit = marketplace.ContentLimit,
            }, cancellationToken);

            return new TranslateResponse
            {
                Text = claudeResponse.Content.Single().Text.Replace("\n", "<br>")
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

        private string GetTranslateTemplate(string language, string description)
        {
            return $"Translate the given description to the {language} Language. description: {description}. Output should be pure translated text formated in HTML language.";
        }

        private string GetDefaultTemplate(string productCategoryName)
        {
            return $"For {productCategoryName} generate creative annotation/description containing the product consistency, how to use, brand information, recommendations and other information. Output should be in paragraphs and in Georgian. Output HTML Language (Small Bold headers, Bullet points, paragraphs, various tags and etc), use br tags instead of \\n;";
        }
    }
}

