using Api24ContentAI.Infrastructure.Repository;
using Api24ContentAI.Infrastructure.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System.Net.Http;
using System.Threading;
using System;
using Api24ContentAI.Infrastructure.Service.Implementations;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Middleware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Api24ContentAI
{
    public class Startup(IConfiguration configuration)
    {
        private IConfiguration Configuration { get; } = configuration;

        public void ConfigureServices(IServiceCollection services)
        {

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            
            _ = services.AddControllers();

            _ = services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Api24ContentAI", Version = "v1" });
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Description = "Please enter a valid token",
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    BearerFormat = "JWT",
                    Scheme = "Bearer"
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new List<string>()
                    }
                });
            });

            services.AddDbContexts(Configuration);
            services.AddRepositories();
            services.AddServices();

            string apiKey = Configuration.GetSection("Security:ClaudeApiKey").Value;

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Claude API Key is missing in configuration.");
            }

            _ = services.AddHttpClient<IClaudeService, ClaudeService>((client) =>
                    {
                        client.DefaultRequestHeaders.Clear();
                        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                        client.DefaultRequestHeaders.Add("anthropic-beta", "max-tokens-3-5-sonnet-2024-07-15");
                        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

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


            _ = services.AddHttpClient<IApi24Service, Api24Service>((client) =>
            {
                client.DefaultRequestHeaders.Add("AccessToken", Configuration.GetSection("Security:Api24AccessToken").Value);

                client.BaseAddress = new Uri("https://stage.api24.ge/v2/");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new SocketsHttpHandler()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(15)
                };
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

            _ = services.Configure<JwtOptions>(Configuration.GetSection("ApiSettings:JwtOptions"));
            _ = services.Configure<FbOptions>(Configuration.GetSection("ApiSettings:FbOptions"));

            _ = services.AddIdentity<User, Role>(Options =>
            {
                Options.Password.RequiredLength = 3;
                Options.Password.RequireNonAlphanumeric = false;
                Options.Password.RequireUppercase = false;
                Options.Password.RequireLowercase = false;
                Options.Password.RequireDigit = false;

                Options.User.RequireUniqueEmail = true;
            })
             .AddEntityFrameworkStores<ContentDbContext>()
             .AddDefaultTokenProviders();

            string secret = Configuration.GetValue<string>("ApiSettings:JwtOptions:Secret");
            string issuer = Configuration.GetValue<string>("ApiSettings:JwtOptions:Issuer");
            string audience = Configuration.GetValue<string>("ApiSettings:JwtOptions:Audience");
            byte[] key = Encoding.ASCII.GetBytes(secret);

            _ = services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,

                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                };
            });

            _ = services.AddHttpClient();
            _ = services.AddAuthorization();

            _ = services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    _ = builder.AllowAnyHeader().AllowAnyOrigin().AllowAnyMethod();
                });
            });

            services.AddScoped<IDocumentTranslationService>(sp => 
                new DocumentTranslationService(
                    sp.GetRequiredService<IClaudeService>(),
                    sp.GetRequiredService<ILanguageService>(),
                    sp.GetRequiredService<IUserRepository>(),
                    sp.GetRequiredService<IGptService>(),
                    sp.GetRequiredService<ILogger<DocumentTranslationService>>()
                )
            );
            services.AddScoped<IGptService, GptService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            _ = env.IsDevelopment() ? app.UseDeveloperExceptionPage() : app.UseGlobalExceptionHandling();
            _ = app.UseDeveloperExceptionPage();
            _ = app.UseSwagger();
            _ = app.UseSwaggerUI(static c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Api24ContentAI v1"));

            _ = app.UseHttpsRedirection();

            _ = app.UseRouting();
            _ = app.UseCors();
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();

            _ = app.UseEndpoints(static endpoints =>
            {
                _ = endpoints.MapControllers();
            });
        }
    }
}
