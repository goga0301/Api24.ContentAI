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

}