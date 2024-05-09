using Api24ContentAI.Infrastructure.Repository;
using Api24ContentAI.Infrastructure.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Net.Http;
using System.Threading;
using System;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Api24ContentAI.Domain.Service;

namespace Api24ContentAI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Api24ContentAI", Version = "v1" });
            });

            services.AddDbContexts(Configuration);
            services.AddRepositories();
            services.AddServices();

            services.AddHttpClient<IClaudeService, ClaudeService>((client) =>
            {
                client.DefaultRequestHeaders.Add("x-api-key", "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");
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new SocketsHttpHandler()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(15)
                };
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Api24ContentAI v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
