using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Api24ContentAI.Domain.Service
{
    public interface IPdfService
    {
        Task<byte[]> ConvertMarkdownToPdf(IFormFile markdownFile, CancellationToken cancellationToken);
        Task<byte[]> ConvertPdfToWord(IFormFile pdfFile, CancellationToken cancellationToken);
        Task<byte[]> ConvertWordToPdf(IFormFile wordFile, CancellationToken cancellationToken);
    }
}

