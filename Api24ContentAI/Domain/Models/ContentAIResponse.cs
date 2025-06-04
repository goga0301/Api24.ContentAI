using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace Api24ContentAI.Domain.Models
{
    public class ContentAIResponse
    {
        public string Text { get; set; }
    }

    public class TranslateResponse
    {
        public string Text { get; set; }
        public byte[] File { get; set; }
    }
    public class ChunkForTranslateResponse
    {
        public List<string> Chunks { get; set; }
    }

    public class CopyrightAIResponse
    {
        public string Text { get; set; }
    }
    public class VideoScriptAIResponse
    {
        public string Text { get; set; }
    }
    
    public class EmailAIResponse
    {
        public string Text { get; set; }
    }   

    public class LawyerResponse
    {
        public string Text { get; set; }
    }

    public class VerificationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string UserId { get; set; }
        public string TranslationId { get; set; }
        public int RecoveredChunks { get; set; }
        public int VerifiedChunks { get; set; }
        public Dictionary<int, string> ChunkWarnings { get; set; } = [];
        public double? QualityScore { get; set; }
        
        public string Feedback { get; set; }
    }
    
    public class EnhanceTranslateResponse
    {
        public string OriginalText { get; set; }
        public string EnhancedText { get; set; }
        public List<string> Suggestion { get; set; }
        public int ChangeCount { get; set; }
        
    }
}
