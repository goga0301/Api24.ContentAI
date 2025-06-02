using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class PdfService(HttpClient httpClient, ILogger<PdfService> logger) : IPdfService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PdfService> _logger = logger;

    public async Task<byte[]> ConvertMarkdownToPdf(IFormFile markdown, CancellationToken cancellation = default)
    {
        if (markdown == null || markdown.Length == 0)
        {
            throw new ArgumentException("Markdown file is required");
        }

        if (!markdown.FileName.EndsWith(".md"))
        {
            throw new ArgumentException("Markdown file must be a .md file");
        }

        try
        {
            using var content = new MultipartFormDataContent();

            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await markdown.CopyToAsync(memoryStream, cancellation);
                fileBytes = memoryStream.ToArray();
            }
            
            var fileContent = new ByteArrayContent(fileBytes);
            content.Add(fileContent, "file", markdown.FileName);
            
            var response = await _httpClient.PostAsync(
                "http://localhost:8000/convert-md-to-pdf/",
                content,
                cancellation);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync(cancellation);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellation);
                _logger.LogError($"PDF conversion failed: {response.StatusCode} - {errorContent}");
                throw new HttpRequestException($"PDF conversion failed: {response.StatusCode} - {errorContent}");
            }

        }
        catch (Exception e)
        {
            _logger.LogError(e, "PDF conversion failed");
            throw new InvalidOperationException($"PDF conversion failed {e.Message}");
        }
    }
}