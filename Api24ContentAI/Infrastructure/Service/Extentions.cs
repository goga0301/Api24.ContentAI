using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace Api24ContentAI.Infrastructure.Service
{
    public static class Extentions
    {
        public static void  AddServices(this IServiceCollection services)
        {
            services.AddScoped<ITemplateService, TemplateService>();
            services.AddScoped<ICustomTemplateService, CustomTemplateService>();
            services.AddScoped<IProductCategoryService, ProductCategoryService>();
            services.AddScoped<IMarketplaceService, MarketplaceService>();
            services.AddScoped<IRequestLogService, RequestLogService>();
            services.AddScoped<ILanguageService, LanguageService>();
            
            services.AddScoped<IClaudeService, ClaudeService>();
            services.AddScoped<IContentService, ContentService>();

            services.AddScoped<IApi24Service, Api24Service>();
        }
    }
}
