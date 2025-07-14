using Api24ContentAI.Domain.Models;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Domain.Service
{
    public interface IUserNameExtractionService
    {
        Task<UserNameExtractionResult> ExtractUserNamesFromFileAsync(IFormFile file, CancellationToken cancellationToken);
        Task<UserNameExtractionResult> ExtractUserNamesFromFileAsync(IFormFile file, string language, CancellationToken cancellationToken);
        bool IsSupportedFileType(string fileExtension);
    }

    public class UserNameExtractionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> UserNames { get; set; } = new List<string>();
        public string FileType { get; set; }
        public string FileName { get; set; }
        public int ExtractedTextLength { get; set; }
        public string ExtractionMethod { get; set; }
    }
} 