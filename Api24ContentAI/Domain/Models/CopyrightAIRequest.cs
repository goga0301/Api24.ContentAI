using System;

namespace Api24ContentAI.Domain.Models
{
    public class CopyrightAIRequest
    {
        public Guid UniqueKey { get; set; }
        public string ProductName { get; set; }
        public int LanguageId { get; set; }
    }  
    
    public class UserCopyrightAIRequest
    {
        public string ProductName { get; set; }
        public int LanguageId { get; set; }
    }

    public class VideoScriptAIRequest
    {
        public Guid UniqueKey { get; set; }
        public string ProductName { get; set; }
        public int LanguageId { get; set; }
    }

    public class UserVideoScriptAIRequest
    {
        public string ProductName { get; set; }
        public int LanguageId { get; set; }
    }
}