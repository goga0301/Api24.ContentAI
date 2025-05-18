using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Models
{
    public enum DocumentFormat
    {
        PDF,
        Word,
        Markdown
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
        public DocumentFormat OutputFormat { get; set; } = DocumentFormat.PDF;
    }
}