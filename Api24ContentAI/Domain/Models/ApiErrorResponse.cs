using System;

namespace Api24ContentAI.Domain.Models;

public class ApiErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; }
    public string DetailedError { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}