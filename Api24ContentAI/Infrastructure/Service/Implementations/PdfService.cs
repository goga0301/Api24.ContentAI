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
            _logger.LogInformation("Starting PDF conversion for file: {FileName}, Size: {FileSize} bytes", 
                markdown.FileName, markdown.Length);
            
            // Check for API key in multiple locations
            var apiKey = _configuration["CONVERT_API_KEY"] ?? 
                         _configuration["ConvertApiKey"] ?? 
                         Environment.GetEnvironmentVariable("CONVERT_API_KEY");
            
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("ConvertApi API key is not configured in any location");
                throw new InvalidOperationException("ConvertApi API key is not configured. Please set CONVERT_API_KEY environment variable or configuration.");
            }
            
            _logger.LogDebug("ConvertApi key found, length: {KeyLength}", apiKey.Length);
            
            // Test temp directory access
            var testTempDir = Path.GetTempPath();
            _logger.LogDebug("Temp directory: {TempDir}", testTempDir);
            
            if (!Directory.Exists(testTempDir))
            {
                _logger.LogError("Temp directory does not exist: {TempDir}", testTempDir);
                throw new InvalidOperationException($"Temp directory not accessible: {testTempDir}");
            }
            
            var convertApi = new ConvertApi(apiKey);
            _logger.LogDebug("ConvertApi instance created successfully");
            
            _logger.LogDebug("Starting ConvertApi conversion from md to pdf");
            var convert = await convertApi.ConvertAsync("md", "pdf",
                new ConvertApiFileParam("File", markdown.OpenReadStream(), markdown.FileName)
            );
            _logger.LogInformation("ConvertApi conversion completed successfully");
            
            tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _logger.LogDebug("Creating temp directory: {TempDirectory}", tempDirectory);
            
            try
            {
                Directory.CreateDirectory(tempDirectory);
                _logger.LogDebug("Temp directory created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create temp directory: {TempDirectory}", tempDirectory);
                throw new InvalidOperationException($"Cannot create temp directory: {tempDirectory}. Check file system permissions.", ex);
            }
            
            _logger.LogDebug("Saving converted files to temp directory");
            try
            {
                await convert.SaveFilesAsync(tempDirectory);
                _logger.LogDebug("Files saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save converted files to: {TempDirectory}", tempDirectory);
                throw new InvalidOperationException($"Cannot save files to temp directory: {tempDirectory}", ex);
            }
            
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
            _logger.LogError(e, "PDF conversion failed. Exception Type: {ExceptionType}, Message: {Message}", 
                e.GetType().FullName, e.Message);
            
            if (e.InnerException != null)
            {
                _logger.LogError("Inner Exception: {InnerExceptionType} - {InnerMessage}", 
                    e.InnerException.GetType().FullName, e.InnerException.Message);
            }
            
            // Log stack trace for better debugging
            _logger.LogError("Stack trace: {StackTrace}", e.StackTrace);
            
            // Provide more specific error messages based on exception type
            var errorMessage = e switch
            {
                UnauthorizedAccessException => "Access denied to file system resources. Check permissions.",
                DirectoryNotFoundException => "Directory not found. Check temp directory permissions.",
                FileNotFoundException => "Required file not found. Check ConvertAPI package deployment.",
                System.Net.Http.HttpRequestException => "Network error connecting to ConvertAPI service.",
                ArgumentException => $"Invalid argument: {e.Message}",
                InvalidOperationException => e.Message,
                _ => $"PDF conversion failed: {e.Message}"
            };
            
            throw new InvalidOperationException(errorMessage, e);
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