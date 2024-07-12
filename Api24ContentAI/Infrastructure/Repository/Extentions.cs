using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Api24ContentAI.Infrastructure.Repository.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api24ContentAI.Infrastructure.Repository
{
    public static class Extentions
    {
        public static void AddDbContexts(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<ContentDbContext>(x =>
            {
                x.UseNpgsql(configuration.GetSection("DatabaseOptions:ConnectionString").Value,
                            b =>
                            {
                                b.MigrationsHistoryTable("EF_Migrations", "ContentDb");
                            });

            });
        }

        public static void AddRepositories(this IServiceCollection services)
        {
            services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
            services.AddScoped<ITemplateRepository, TemplateRepository>();
            services.AddScoped<ICustomTemplateRepository, CustomTemplateRepository>();
            services.AddScoped<IMarketplaceRepository, MarketplaceRepository>();
            services.AddScoped<IRequestLogRepository, RequestLogRepository>();
            services.AddScoped<IUserRequestLogRepository, UserRequestLogRepository>();
            services.AddScoped<ILanguageRepository, LanguageRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
        }
    }
}
