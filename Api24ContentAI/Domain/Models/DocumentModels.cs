using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Models
{
    public enum DocumentFormat
    {
        Pdf = 0,
        Word = 1,
        Markdown = 2,
        Srt = 3,
        Txt = 4
    }
    
    public class DocumentConversionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string Content { get; set; }
        public byte[] FileData { get; set; }
        public string FileName { get; set; }
        public DocumentFormat OutputFormat { get; set; }
        public string ContentType { get; set; }
    }
    
    public class DocumentTranslationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string OriginalContent { get; set; }
        public string TranslatedContent { get; set; }
        public byte[] FileData { get; set; }
        public string FileName { get; set; }
        public DocumentFormat OutputFormat { get; set; }
        public string ContentType { get; set; }
        public double TranslationQualityScore { get; set; }
        public string TranslationId { get; set; }
        public decimal Cost { get; set; }
        public List<TranslationSuggestion> Suggestions { get; set; } = new List<TranslationSuggestion>();
    }
    
    public class DocumentTranslationRequest
    {
        public IFormFile File { get; set; }
        public int TargetLanguageId { get; set; }
        public DocumentFormat OutputFormat { get; set; } = DocumentFormat.Pdf;
    }

    public class DocumentConvertRequest
    {
        public IFormFile File { get; set; }
    }

    public class ScreenShotResult
    {
        public List<PageScreenshot> Pages { get; set; }
    }

    public class PageScreenshot
    {
        public int Page { get; set; }
        public List<string> ScreenShots { get; set; }
    }
    
    public class SrtEntry
    {
        public int SequenceNumber { get; set; }
        public string Timestamp { get; set; }
        public string Text { get; set; }
    }
    
    public class TranslationVerificationResult
    {
        public bool Success { get; set; }
        public double? QualityScore { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // New models for suggestions feature
    public class TranslationSuggestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string Description { get; set; }
        public SuggestionType Type { get; set; }
        public string OriginalText { get; set; }
        public string SuggestedText { get; set; }
        public int Priority { get; set; } // 1 = High, 2 = Medium, 3 = Low
    }
    
    public enum SuggestionType
    {
        GrammarError = 1,
        SyntaxError = 2,
        StyleImprovement = 3,
        Terminology = 4,
        Punctuation = 5,
        Formatting = 6,
        Clarity = 7,
        Consistency = 8
    }
    
    public class ApplySuggestionRequest
    {
        public string TranslatedContent { get; set; }
        public string SuggestionId { get; set; }
        public TranslationSuggestion Suggestion { get; set; }
        public int TargetLanguageId { get; set; }
    }
    
    public class ApplySuggestionResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string UpdatedContent { get; set; }
        public string ChangeDescription { get; set; }
        public List<TranslationSuggestion> NewSuggestions { get; set; } = new List<TranslationSuggestion>();
    }
}