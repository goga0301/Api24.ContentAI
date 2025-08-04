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
    
    public enum AIModel
    {
        Claude4Sonnet = 0,
        Claude37Sonnet = 1,
        Gemini25Pro = 2
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
        public int OutputLanguageId {get; set; }
        public DocumentFormat OutputFormat { get; set; } = DocumentFormat.Pdf;
        public AIModel Model { get; set; } = AIModel.Claude4Sonnet;
    }

    public class DocumentConvertRequest
    {
        public IFormFile File { get; set; }
    }

    public class ScreenShotResult
    {
        public List<PageScreenshot> Pages { get; set; }
        public string Method { get; set; } = "screenshot"; // "screenshot" or "text_extraction"
    }

    public class PageScreenshot
    {
        public int Page { get; set; }
        public List<string> ScreenShots { get; set; }
        public string Text { get; set; } = string.Empty; // For text extraction fallback
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

    public class TranslationSuggestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Title { get; set; }
        public string? Description { get; set; }
        public SuggestionType Type { get; set; }
        public string? OriginalText { get; set; }
        public string? SuggestedText { get; set; }
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
        public int OutputLanguageId {get; set; }
        public string? EditedOriginalText { get; set; }
        public string? EditedSuggestedText { get; set; }
        
        public static ApplySuggestionRequest FromOriginalSuggestion(
            string translatedContent, 
            TranslationSuggestion suggestion, 
            int targetLanguageId)
        {
            return new ApplySuggestionRequest
            {
                TranslatedContent = translatedContent,
                SuggestionId = suggestion.Id,
                Suggestion = suggestion,
                TargetLanguageId = targetLanguageId
            };
        }
        
        public static ApplySuggestionRequest WithEditedText(
            string translatedContent,
            TranslationSuggestion originalSuggestion,
            int targetLanguageId,
            string editedOriginalText,
            string editedSuggestedText)
        {
            return new ApplySuggestionRequest
            {
                TranslatedContent = translatedContent,
                SuggestionId = originalSuggestion.Id,
                Suggestion = originalSuggestion,
                TargetLanguageId = targetLanguageId,
                EditedOriginalText = editedOriginalText,
                EditedSuggestedText = editedSuggestedText
            };
        }
        
        public bool HasEdits => !string.IsNullOrEmpty(EditedOriginalText) || 
                               !string.IsNullOrEmpty(EditedSuggestedText);
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

public class PageCountRequest
{
    public IFormFile File { get; set; }
}

public class PageCountResponse
{
    public bool Success { get; set; }
    public int PageCount { get; set; }
    public string? FileName { get; set; }
    public string? ErrorMessage { get; set; }
}
