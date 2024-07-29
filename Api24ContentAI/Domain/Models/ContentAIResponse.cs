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
}
