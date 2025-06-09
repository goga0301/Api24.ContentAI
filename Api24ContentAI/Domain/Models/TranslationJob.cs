using System;

namespace Api24ContentAI.Domain.Models
{
    public class TranslationJob
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = "Processing"; // Pending, Processing, Completed, Failed
        public int Progress { get; set; } = 0;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public byte[]? ResultData { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public string? ErrorMessage { get; set; }
        public int EstimatedTimeMinutes { get; set; }
        public string FileType { get; set; } = string.Empty;
        public long FileSizeKB { get; set; }
    }
} 