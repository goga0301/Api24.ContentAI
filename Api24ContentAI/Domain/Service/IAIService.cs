using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IAIService
    {
        Task<AIResponse> SendTextRequest(string prompt, AIModel model, CancellationToken cancellationToken);
        Task<AIResponse> SendRequestWithImages(string prompt, List<AIImageData> images, AIModel model, CancellationToken cancellationToken);
        Task<AIResponse> SendRequestWithFile(List<ContentFile> contentFiles, AIModel model, CancellationToken cancellationToken);
    }

    public class AIResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string ErrorMessage { get; set; }
        public AIModel UsedModel { get; set; }
    }

    public class AIImageData
    {
        public string Base64Data { get; set; }
        public string MimeType { get; set; } = "image/png";
    }
} 