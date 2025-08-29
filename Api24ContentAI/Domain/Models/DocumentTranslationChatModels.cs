using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Models
{
    public class StartDocumentTranslationChatRequest
    {
        public IFormFile File { get; set; }
        public int TargetLanguageId { get; set; }
        public DocumentFormat OutputFormat { get; set; } = DocumentFormat.Pdf;
        public AIModel Model { get; set; } = AIModel.Claude4Sonnet;
        public string? InitialMessage { get; set; } 
    }

    public class ContinueDocumentTranslationChatRequest
    {
        public string ChatId { get; set; }
        public string Message { get; set; }
        public string MessageType { get; set; } = "UserRequest"; 
        public string? SuggestionId { get; set; } 
        public string? TranslationJobId { get; set; } 
    }

    public class GetDocumentTranslationChatRequest
    {
        public string ChatId { get; set; }
        public int PageSize { get; set; } = 50;
        public int PageNumber { get; set; } = 1;
        public bool IncludeMetadata { get; set; } = false;
    }

    public class DocumentTranslationChatResponse
    {
        public string ChatId { get; set; }
        public string Status { get; set; } // Processing, Completed, Failed
        public string Title { get; set; }
        public string OriginalFileName { get; set; }
        public string FileType { get; set; }
        public string TargetLanguageName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DocumentTranslationResult? TranslationResult { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DocumentTranslationChatListResponse
    {
        public List<DocumentTranslationChatSummary> Chats { get; set; } = new List<DocumentTranslationChatSummary>();
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int PageNumber { get; set; }
        public bool HasNextPage => (PageNumber * PageSize) < TotalCount;
    }

    public class DocumentTranslationChatSummary
    {
        public string ChatId { get; set; }
        public string Title { get; set; }
        public string OriginalFileName { get; set; }
        public string FileType { get; set; }
        public string TargetLanguageName { get; set; }
        public string Status { get; set; } // Processing, Completed, Failed
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public bool HasResult { get; set; }
        public bool HasError { get; set; }
    }

    public class DocumentTranslationChatMessageModel
    {
        public Guid Id { get; set; }
        public string MessageType { get; set; }
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? TranslationJobId { get; set; }
        public string? AIModel { get; set; }
        public decimal? ProcessingCost { get; set; }
        public int? ProcessingTimeSeconds { get; set; }
        public MessageMetadata? Metadata { get; set; }
        public bool IsVisible { get; set; }
    }

    public class CreateDocumentTranslationChatModel
    {
        public string UserId { get; set; }
        public string OriginalFileName { get; set; }
        public string? OriginalContentType { get; set; }
        public long OriginalFileSizeBytes { get; set; }
        public string FileType { get; set; }
        public int TargetLanguageId { get; set; }
        public string? TargetLanguageName { get; set; }
        public string? InitialMessage { get; set; }
    }

    public class AddChatMessageModel
    {
        public string ChatId { get; set; }
        public string MessageType { get; set; }
        public string UserId { get; set; }
        public string? Content { get; set; }
        public string? TranslationJobId { get; set; }
        public string? AIModel { get; set; }
        public decimal? ProcessingCost { get; set; }
        public int? ProcessingTimeSeconds { get; set; }
        public MessageMetadata? Metadata { get; set; }
    }

    public class MessageMetadata
    {
        public string? OriginalContent { get; set; }
        public string? TranslatedContent { get; set; }
        public List<TranslationSuggestion>? Suggestions { get; set; }
        public string? ErrorDetails { get; set; }
        public FileProcessingInfo? FileInfo { get; set; }
        public TranslationStats? Stats { get; set; }
    }

    public class FileProcessingInfo
    {
        public string? Method { get; set; } 
        public int? PageCount { get; set; }
        public string? OutputFormat { get; set; }
        public long? ProcessingTimeMs { get; set; }
        public string? QualityScore { get; set; }
    }

    public class TranslationStats
    {
        public int OriginalCharacterCount { get; set; }
        public int TranslatedCharacterCount { get; set; }
        public double? QualityScore { get; set; }
        public int SuggestionCount { get; set; }
        public string? SourceLanguage { get; set; }
        public string? TargetLanguage { get; set; }
    }

    // Utility models
    public class DocumentTranslationChatFilter
    {
        public string? UserId { get; set; }
        public string? Status { get; set; }
        public string? FileType { get; set; }
        public int? TargetLanguageId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int PageSize { get; set; } = 20;
        public int PageNumber { get; set; } = 1;
        public string SortBy { get; set; } = "LastActivityAt";
        public string SortDirection { get; set; } = "DESC";
    }
} 
