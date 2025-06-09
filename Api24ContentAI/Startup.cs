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
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Middleware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;


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
                        client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,max-tokens-3-5-sonnet-2024-07-15");
                        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                        client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
                        client.Timeout = TimeSpan.FromMinutes(15); // Extended timeout for large translations
                    })
            .ConfigurePrimaryHttpMessageHandler(() =>
                    {
                        return new SocketsHttpHandler()
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                            RequestHeaderEncodingSelector = (_, _) => System.Text.Encoding.UTF8
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
                var allowedOrigins = Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>() 
                                   ?? new[] { "http://localhost:3000" };

                options.AddPolicy("AllowSpecificOrigins", builder =>
                {
                    builder.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials()
                           .SetPreflightMaxAge(TimeSpan.FromMinutes(5));
                });

                // Development-only policy (more permissive but still secure)
                options.AddPolicy("AllowAllForDevelopment", builder =>
                {
                    builder.SetIsOriginAllowed(origin => 
                    {
                        if (string.IsNullOrEmpty(origin)) return false;
                        
                        // Allow localhost with any port
                        var uri = new Uri(origin);
                        return uri.Host == "localhost" || 
                               uri.Host == "127.0.0.1" || 
                               uri.Host.EndsWith(".local") ||
                               allowedOrigins.Any(o => o.Equals(origin, StringComparison.OrdinalIgnoreCase));
                    })
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                });
            });
            
            services.AddScoped<IDocumentTranslationService>(sp => 
                new DocumentTranslationService(
                    sp.GetRequiredService<ILanguageService>(),
                    sp.GetRequiredService<IFileProcessorFactory>(),
                    sp.GetRequiredService<ILogger<DocumentTranslationService>>()
                )
            );
            services.AddScoped<ITranslationJobService, TranslationJobService>();
            
            services.AddHostedService<TranslationJobCleanupService>();
            services.AddScoped<IGptService, GptService>();
            services.AddScoped<IPdfService, PdfService>();
            services.AddScoped<ITextProcessor, TextProcessor>();
            services.AddScoped<IWordProcessor, WordProcessor>();
            services.AddScoped<IPdfProcessor, PdfProcessor>(); 
            services.AddScoped<ISrtProcessor, SrtProcessor>();
            
            services.AddScoped<IFileProcessor>(provider => provider.GetService<ITextProcessor>());
            services.AddScoped<IFileProcessor>(provider => provider.GetService<IWordProcessor>());
            services.AddScoped<IFileProcessor>(provider => provider.GetService<IPdfProcessor>());
            services.AddScoped<IFileProcessor>(provider => provider.GetService<ISrtProcessor>());
            
            services.AddScoped<IFileProcessorFactory, FileProcessorFactory>();
            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => 
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Api24ContentAI v1");
                    c.RoutePrefix = "swagger";
                });
            }
            else
            {
                app.UseGlobalExceptionHandling();
                app.UseSwagger();
                app.UseSwaggerUI(c => 
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Api24ContentAI v1");
                    c.RoutePrefix = "swagger";
                });
            }

            // Only use HTTPS redirection in production
            if (!env.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }

            // Use different CORS policies based on environment
            if (env.IsDevelopment())
            {
                app.UseCors("AllowAllForDevelopment");
            }
            else
            {
                app.UseCors("AllowSpecificOrigins");
            }

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                // Add a default route to redirect to Swagger
                endpoints.MapGet("/", context =>
                {
                    context.Response.Redirect("/swagger");
                    return Task.CompletedTask;
                });
            });
        }
    }
}
