using Microsoft.AspNetCore.Builder;

namespace Api24ContentAI.Infrastructure.Middleware
{
    public static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
        {
            return app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
      }
    }
}