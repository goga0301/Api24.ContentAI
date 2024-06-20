using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
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

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);

            var templateText = GetDefaultTemplate(productCategory.NameEng, language.Name);

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


            var claudRequestContent = $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

            var claudeRequest = new ClaudeRequest(claudRequestContent);

            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
            var lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
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
                Text = claudResponseText
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
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            var lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }
            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
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
                Text = claudResponseText
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
            return $"You are a multilingual AI translation assistant. Your task is to translate product descriptions from one language to another and output the result in HTML format. The source language for the product description is: <source_language> {{ENGLISH}} </source_language>. The target language for the product description is: <target_language> {language} </target_language>. Here is the product description text to translate: <product_description> {description} </product_description>. Please translate the product description text from {{ENGLISH}} to {language}. Output the translated text in HTML format, enclosed within <translated_description> tags. Do not include any other explanations, notes, or caveats in your output. Only provide the translated text in the specified HTML tags.";
        }
        // {
        //     return $"Translate the given description to the {language} Language. description: {description}. Output should be pure translated text formated in HTML language.";
        // }

        private string GetDefaultTemplate(string productCategoryName, string language)
        {
            return $"For {productCategoryName} generate creative annotation/description containing the product consistency, how to use, brand information, recommendations and other information. Output should be in paragraphs and in {language}. Output HTML Language (Small Bold headers, Bullet points, paragraphs, various tags and etc), use br tags instead of \\n;";
        }
    }
}

