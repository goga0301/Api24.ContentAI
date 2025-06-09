using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api24ContentAI.Domain.Entities
{
    [Table("TranslationJobs")]
    public class TranslationJobEntity : BaseEntity
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        [Required]
        [MaxLength(50)]
        public string JobId { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Processing"; // Processing, Completed, Failed
        
        public int Progress { get; set; } = 0;
        
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        
        public byte[]? ResultData { get; set; }
        
        [MaxLength(255)]
        public string? FileName { get; set; }
        
        [MaxLength(100)]
        public string? ContentType { get; set; }
        
        [MaxLength(500)]
        public string? ErrorMessage { get; set; }
        
        public int EstimatedTimeMinutes { get; set; }
        
        [Required]
        [MaxLength(10)]
        public string FileType { get; set; } = string.Empty;
        
        public long FileSizeKB { get; set; }
        
        [Required]
        [MaxLength(450)] 
        public string UserId { get; set; } = string.Empty;
        
        public DateTime? CompletedAt { get; set; }
        
        // Index for cleanup operations - expire after 2 hours
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);
    }
} 