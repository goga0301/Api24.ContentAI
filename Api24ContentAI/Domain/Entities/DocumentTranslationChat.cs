using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api24ContentAI.Domain.Entities
{
    [Table("DocumentTranslationChats")]
    public class DocumentTranslationChat : BaseEntity
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [Required]
        [MaxLength(50)]
        public string ChatId { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty; [MaxLength(100)]
        public string? OriginalContentType { get; set; }
        
        public long OriginalFileSizeBytes { get; set; }
        
        [Required]
        [MaxLength(10)]
        public string FileType { get; set; } = string.Empty; 
        
        public int TargetLanguageId { get; set; }
        
        [MaxLength(100)]
        public string? TargetLanguageName { get; set; }
        
        [MaxLength(20)]
        public string Status { get; set; } = "Processing"; // Processing, Completed, Failed
        
        [MaxLength(255)]
        public string? Title { get; set; } 
        
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
        
        public string? TranslationResult { get; set; }
        
        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }
    }
    
    [Table("DocumentTranslationChatMessages")]
    public class DocumentTranslationChatMessage : BaseEntity
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [Required]
        [MaxLength(50)]
        public Guid ChatId { get; set; }
        
        [Required]
        [MaxLength(20)]
        public string MessageType { get; set; } = string.Empty; 
        
        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        
        public string? Content { get; set; } 
        
        public string? Metadata { get; set; } 
        
        [MaxLength(50)]
        public string? TranslationJobId { get; set; }
        
        [MaxLength(20)]
        public string? AIModel { get; set; } 
        
        public decimal? ProcessingCost { get; set; }
        
        public int? ProcessingTimeSeconds { get; set; }
        
        public bool IsVisible { get; set; } = true;

        [ForeignKey(nameof(ChatId))]
        public DocumentTranslationChat Chat { get; set; }
    }
    
    public enum ChatMessageType
    {
        UserRequest = 1,
        SystemResponse = 2,
        TranslationResult = 3,
        SuggestionApplied = 4,
        ErrorMessage = 5,
        StatusUpdate = 6
    }
} 
