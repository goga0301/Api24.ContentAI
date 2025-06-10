using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Net;
using Api24ContentAI.Domain.Models;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Api24ContentAI.Infrastructure.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    
    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        (HttpStatusCode statusCode, string message) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, "Bad request"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized"),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Not found"),
            Exception => (HttpStatusCode.InternalServerError, "Internal server error")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new ApiErrorResponse
        {
            StatusCode = context.Response.StatusCode,
            Message = message,
            DetailedError = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ||
                          Environment.GetEnvironmentVariable("SHOW_DETAILED_ERRORS") == "true"
                ? exception.ToString()
                : $"Error Type: {exception.GetType().Name}, Message: {exception.Message}"
        };
        
        var jsonResponse = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(jsonResponse);
    }
}