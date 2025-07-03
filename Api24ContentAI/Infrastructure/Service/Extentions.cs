using Api24ContentAI.Domain.Service;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Microsoft.Extensions.DependencyInjection;

namespace Api24ContentAI.Infrastructure.Service
{
    public static class Extentions
    {
        public static void AddServices(this IServiceCollection services)
        {
            // Remove the cache service registration
            // services.AddScoped<ICacheService, RedisCacheService>();
            
            services.AddScoped<ITemplateService, TemplateService>();
            services.AddScoped<ICustomTemplateService, CustomTemplateService>();
            services.AddScoped<IProductCategoryService, ProductCategoryService>();
            services.AddScoped<IMarketplaceService, MarketplaceService>();
            services.AddScoped<IRequestLogService, RequestLogService>();
            services.AddScoped<IUserRequestLogService, UserRequestLogService>();
            services.AddScoped<ILanguageService, LanguageService>();

            services.AddScoped<IClaudeService, ClaudeService>();
            services.AddScoped<IGeminiService, GeminiService>();
            services.AddScoped<IAIService, AIService>();
            services.AddScoped<IContentService, ContentService>();
            services.AddScoped<IUserContentService, UserContentService>();
            services.AddScoped<IUserService, UserService>();

            services.AddScoped<IApi24Service, Api24Service>();

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IJwtGenerator, JwtTokenGenerator>();
            services.AddScoped<IDocumentSuggestionService, DocumentSuggestionService>();

            services.AddHttpContextAccessor();
        }
    }
}
