using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Service;
using ConvertApiDotNet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class PdfService(ILogger<PdfService> logger, IConfiguration configuration) : IPdfService
{
    private readonly ILogger<PdfService> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

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

        string tempFilePath = null;
        string tempDirectory = null;
        
        try
        {
            var apiKey = _configuration["CONVERT_API_KEY"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("ConvertApi API key is not configured. Please set it using: dotnet user-secrets set \"ConvertApiKey\" \"your-api-key\"");
            }
            
            var convertApi = new ConvertApi(apiKey);
            
            var convert = await convertApi.ConvertAsync("md", "pdf",
                new ConvertApiFileParam("File", markdown.OpenReadStream(), markdown.FileName)
            );
            
            tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);
            
            await convert.SaveFilesAsync(tempDirectory);
            
            var pdfFiles = Directory.GetFiles(tempDirectory, "*.pdf");
            if (pdfFiles.Length == 0)
            {
                throw new InvalidOperationException("No PDF file was generated");
            }
            
            tempFilePath = pdfFiles[0];
            
            var pdfBytes = await File.ReadAllBytesAsync(tempFilePath, cancellation);
            
            return pdfBytes;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "PDF conversion failed");
            throw new InvalidOperationException($"PDF conversion failed: {e.Message}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.LogDebug("Temporary file deleted: {TempFilePath}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file: {TempFilePath}", tempFilePath);
                }
            }
            
            if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                    _logger.LogDebug("Temporary directory deleted: {TempDirectory}", tempDirectory);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary directory: {TempDirectory}", tempDirectory);
                }
            }
        }
    }
}