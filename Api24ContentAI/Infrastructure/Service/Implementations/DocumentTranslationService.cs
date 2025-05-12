using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PuppeteerSharp;
using Markdig;
using PuppeteerSharp.Media;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationService : IDocumentTranslationService
    {
        private readonly IClaudeService _claudeService;
        private readonly ILanguageService _languageService;
        private readonly IUserRepository _userRepository;
        private readonly IRequestLogService _requestLogService;
        private readonly ICacheService _cacheService;
        private readonly IGptService _gptService;

        public DocumentTranslationService(
            IClaudeService claudeService,
            ILanguageService languageService,
            IUserRepository userRepository,
            IRequestLogService requestLogService,
            ICacheService cacheService,
            IGptService gptService)
        {
            _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _requestLogService = requestLogService ?? throw new ArgumentNullException(nameof(requestLogService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
        }

        public async Task<DocumentConversionResult> ConvertToMarkdown(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No file provided" };

                string fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                string fileContent;

                switch (fileExtension)
                {
                    case ".pdf":
                        fileContent = await ExtractTextFromPdfAsync(file, cancellationToken);
                        break;
                    case ".docx":
                    case ".doc":
                        fileContent = await ExtractTextFromWordAsync(file, cancellationToken);
                        break;
                    default:
                        return new DocumentConversionResult { Success = false, ErrorMessage = "Unsupported file format. Please provide a PDF or Word document." };
                }

                if (string.IsNullOrWhiteSpace(fileContent))
                {
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No content could be extracted from the file" };
                }

                string markdownContent = await ConvertToMarkdownWithClaude(fileContent, file, cancellationToken);

                return new DocumentConversionResult
                {
                    Success = true,
                    Content = markdownContent,
                    OutputFormat = Domain.Models.DocumentFormat.Markdown,
                    FileName = Path.GetFileNameWithoutExtension(file.FileName) + ".md",
                    ContentType = "text/markdown"
                };
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ConvertToMarkdown", ex, cancellationToken);
                return new DocumentConversionResult { Success = false, ErrorMessage = $"Error converting document to markdown: {ex.Message}" };
            }
        }

        public async Task<DocumentTranslationResult> TranslateMarkdown(string markdownContent, int targetLanguageId, string userId, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdownContent))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No markdown content provided" };
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };
                }

                decimal requestPrice = CalculateTranslationPrice(markdownContent);
                User user = await _userRepository.GetById(userId, cancellationToken);

                if (user == null)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User not found" };
                }

                if (user.UserBalance?.Balance < requestPrice)
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Insufficient balance for translation" };

                LanguageModel language = await _languageService.GetById(targetLanguageId, cancellationToken);
                if (language == null)
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Target language not found" };

                List<string> chunks = GetChunksOfMarkdown(markdownContent);
                if (chunks.Count == 0)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Failed to split content into chunks" };
                }

                var translationTasks = chunks.Select((chunk, index) => 
                    TranslateMarkdownChunkAsync(index, chunk, language.Name, cancellationToken)).ToList();

                var translationResults = await Task.WhenAll(translationTasks);
                string translatedMarkdown = string.Join("\n\n", translationResults.OrderBy(r => r.Key).Select(r => r.Value));

                if (string.IsNullOrWhiteSpace(translatedMarkdown))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "Translation resulted in empty content" };
                }

                VerificationResult verificationResult = await _gptService.VerifyTranslationBatch(
                    translationResults.ToList(), cancellationToken);

                string translationId = Guid.NewGuid().ToString();
                await _requestLogService.Create(new CreateRequestLogModel
                {
                    Request = JsonSerializer.Serialize(new { TargetLanguageId = targetLanguageId, ContentLength = markdownContent.Length, ChunkCount = chunks.Count }),
                    Response = JsonSerializer.Serialize(new { TranslationId = translationId, TranslatedLength = translatedMarkdown.Length, QualityScore = verificationResult.QualityScore ?? (verificationResult.Success ? 1.0 : 0.0) }),
                    RequestType = RequestType.Translate
                }, cancellationToken);

                await _userRepository.UpdateUserBalance(userId, -requestPrice, cancellationToken);

                return new DocumentTranslationResult
                {
                    Success = true,
                    OriginalContent = markdownContent,
                    TranslatedContent = translatedMarkdown,
                    OutputFormat = Domain.Models.DocumentFormat.Markdown,
                    FileName = $"translated_{translationId}.md",
                    ContentType = "text/markdown",
                    TranslationQualityScore = verificationResult.QualityScore ?? (verificationResult.Success ? 1.0 : 0.0),
                    TranslationId = translationId,
                    Cost = requestPrice
                };
            }
            catch (Exception ex)
            {
                await LogErrorAsync("TranslateMarkdown", ex, cancellationToken);
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating markdown: {ex.Message}" };
            }
        }

        
        public async Task<DocumentConversionResult> ConvertFromMarkdown(string markdownContent, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdownContent))
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No markdown content provided" };

                if (outputFormat == Domain.Models.DocumentFormat.Markdown)
                    return new DocumentConversionResult
                    {
                        Success = true,
                        Content = markdownContent,
                        OutputFormat = Domain.Models.DocumentFormat.Markdown,
                        FileName = $"document_{Guid.NewGuid()}.md",
                        ContentType = "text/markdown"
                    };

                byte[] fileData;
                string contentType;
                string fileName;

                if (outputFormat == Domain.Models.DocumentFormat.PDF)
                {
                    (fileData, contentType, fileName) = await ConvertMarkdownToPdfAsync(markdownContent, cancellationToken);
                }
                else if (outputFormat == Domain.Models.DocumentFormat.Word)
                {
                    (fileData, contentType, fileName) = await ConvertMarkdownToWordAsync(markdownContent, cancellationToken);
                }
                else
                {
                    return new DocumentConversionResult { Success = false, ErrorMessage = "Unsupported output format" };
                }

                if (fileData == null || fileData.Length == 0)
                {
                    return new DocumentConversionResult { Success = false, ErrorMessage = "Failed to generate output file" };
                }

                return new DocumentConversionResult
                {
                    Success = true,
                    FileData = fileData,
                    OutputFormat = outputFormat,
                    FileName = fileName,
                    ContentType = contentType
                };
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ConvertFromMarkdown", ex, cancellationToken);
                return new DocumentConversionResult { Success = false, ErrorMessage = $"Error converting from markdown: {ex.Message}" };
            }
        }

        public async Task<DocumentTranslationResult> TranslateDocument(IFormFile file, int targetLanguageId, string userId, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "No file provided" };
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return new DocumentTranslationResult { Success = false, ErrorMessage = "User ID is required" };
                }

                DocumentConversionResult conversionResult = await ConvertToMarkdown(file, cancellationToken);
                if (!conversionResult.Success)
                    return new DocumentTranslationResult { Success = false, ErrorMessage = conversionResult.ErrorMessage };

                DocumentTranslationResult translationResult = await TranslateMarkdown(
                    conversionResult.Content, targetLanguageId, userId, cancellationToken);
                if (!translationResult.Success)
                    return translationResult;

                if (outputFormat != Domain.Models.DocumentFormat.Markdown)
                {
                    DocumentConversionResult reconversionResult = await ConvertFromMarkdown(
                        translationResult.TranslatedContent, outputFormat, cancellationToken);
                    if (!reconversionResult.Success)
                        return new DocumentTranslationResult
                        {
                            Success = false,
                            ErrorMessage = reconversionResult.ErrorMessage,
                            OriginalContent = conversionResult.Content,
                            TranslatedContent = translationResult.TranslatedContent,
                            TranslationQualityScore = translationResult.TranslationQualityScore,
                            TranslationId = translationResult.TranslationId,
                            Cost = translationResult.Cost
                        };

                    translationResult.FileData = reconversionResult.FileData;
                    translationResult.FileName = reconversionResult.FileName;
                    translationResult.ContentType = reconversionResult.ContentType;
                    translationResult.OutputFormat = outputFormat;
                }

                return translationResult;
            }
            catch (Exception ex)
            {
                await LogErrorAsync("TranslateDocument", ex, cancellationToken);
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error in document translation workflow: {ex.Message}" };
            }
        }

        #region Helper Methods

        private async Task<string> ExtractTextFromPdfAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var builder = new StringBuilder();
            byte[] bytes = await FileToByteArrayAsync(file, cancellationToken);

            using (var stream = new MemoryStream(bytes))
            using (var pdfReader = new PdfReader(stream))
            using (var pdf = new PdfDocument(pdfReader))
            {
                int pageCount = pdf.GetNumberOfPages();
                for (int i = 1; i <= pageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string text = PdfTextExtractor.GetTextFromPage(pdf.GetPage(i));
                    builder.AppendLine(text);
                }
            }

            return builder.ToString();
        }

        private async Task<string> ExtractTextFromWordAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            byte[] fileBytes = await FileToByteArrayAsync(file, cancellationToken);
            string base64File = Convert.ToBase64String(fileBytes);

            var fileMessage = new ContentFile
            {
                Type = "file",
                Source = new Source { Type = "base64", MediaType = file.ContentType, Data = base64File }
            };

            var promptMessage = new ContentFile
            {
                Type = "text",
                Text = "Extract all text from this Word document as plain text, preserving paragraphs and headings."
            };

            var claudeRequest = new ClaudeRequestWithFile([fileMessage, promptMessage ]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service");
            }
            
            return content.Text;
        }

        private async Task<string> ConvertToMarkdownWithClaude(string documentContent, IFormFile originalFile, CancellationToken cancellationToken)
        {
            var contents = new List<ContentFile>();

            if (originalFile != null && originalFile.Length > 0)
            {
                byte[] fileBytes = await FileToByteArrayAsync(originalFile, cancellationToken);
                string base64File = Convert.ToBase64String(fileBytes);
                contents.Add(new ContentFile
                {
                    Type = "file",
                    Source = new Source { Type = "base64", MediaType = originalFile.ContentType, Data = base64File }
                });
            }
            else
            {
                contents.Add(new ContentFile { Type = "text", Text = documentContent });
            }

            contents.Add(new ContentFile { Type = "text", Text = GetDocumentToMarkdownTemplate() });

            var claudeRequest = new ClaudeRequestWithFile(contents);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service");
            }
            
            return ExtractContent(content.Text, "<markdown>", "</markdown>");
        }

        private List<string> GetChunksOfMarkdown(string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new List<string>();
            }

            var pipeline = new MarkdownPipelineBuilder().Build();
            var document = Markdig.Markdown.Parse(markdownContent, pipeline);
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();
            int maxChunkSize = 8000; // Set a reasonable chunk size

            foreach (var node in document)
            {
                string nodeText = node.ToString();
                
                // Start a new chunk if we hit a major heading or chunk size limit
                if ((node is Markdig.Syntax.HeadingBlock heading && heading.Level <= 2 && currentChunk.Length > 0) ||
                    (currentChunk.Length + nodeText.Length > maxChunkSize && currentChunk.Length > 0))
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
                
                currentChunk.AppendLine(nodeText);
            }

            if (currentChunk.Length > 0)
                chunks.Add(currentChunk.ToString());

            // If no chunks were created (no headings found), split by paragraphs
            if (chunks.Count == 0 && markdownContent.Length > maxChunkSize)
            {
                return SplitByParagraphs(markdownContent, maxChunkSize);
            }

            return chunks;
        }

        private List<string> SplitByParagraphs(string content, int maxChunkSize)
        {
            var chunks = new List<string>();
            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.None);
            var currentChunk = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (currentChunk.Length + paragraph.Length > maxChunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                }
                
                currentChunk.AppendLine(paragraph);
                currentChunk.AppendLine(); // Add an extra line between paragraphs
            }

            if (currentChunk.Length > 0)
                chunks.Add(currentChunk.ToString());

            return chunks;
        }

        private async Task<KeyValuePair<int, string>> TranslateMarkdownChunkAsync(int order, string markdownChunk, string targetLanguage, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(markdownChunk))
            {
                return new KeyValuePair<int, string>(order, string.Empty);
            }

            // Try to get from cache first
            string cacheKey = $"translate:{targetLanguage}:{markdownChunk.GetHashCode()}";
            string cachedTranslation = await _cacheService.GetAsync<string>(cacheKey, cancellationToken);
            
            if (!string.IsNullOrEmpty(cachedTranslation))
            {
                return new KeyValuePair<int, string>(order, cachedTranslation);
            }

            var message = new ContentFile { Type = "text", Text = GetMarkdownTranslateTemplate(targetLanguage, markdownChunk) };
            var claudeRequest = new ClaudeRequestWithFile([message ]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service for translation");
            }
            
            string translatedChunk = ExtractContent(content.Text, "<translation>", "</translation>");
            
            
            return new KeyValuePair<int, string>(order, translatedChunk);
        }

        private async Task<(byte[] fileData, string contentType, string fileName)> ConvertMarkdownToPdfAsync(string markdownContent, CancellationToken cancellationToken)
        {
            string html = await ConvertMarkdownToHtmlWithClaude(markdownContent, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new InvalidOperationException("Failed to convert markdown to HTML");
            }
            
            byte[] pdfBytes = await ConvertHtmlToPdfAsync(html, cancellationToken);
            return (pdfBytes, "application/pdf", $"document_{Guid.NewGuid()}.pdf");
        }

        private async Task<(byte[] fileData, string contentType, string fileName)> ConvertMarkdownToWordAsync(string markdownContent, CancellationToken cancellationToken)
        {
            string html = await ConvertMarkdownToWordHtmlWithClaude(markdownContent, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new InvalidOperationException("Failed to convert markdown to Word HTML");
            }
            
            byte[] docxBytes = await ConvertHtmlToWordAsync(html, cancellationToken);
            return (docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"document_{Guid.NewGuid()}.docx");
        }

        private async Task<string> ConvertMarkdownToHtmlWithClaude(string markdownContent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                throw new ArgumentException("Markdown content cannot be empty", nameof(markdownContent));
            }

            string promptText = $@"
                Convert this markdown to clean HTML with proper styling:
                ```markdown
                {markdownContent}
                ```
                Return ONLY the HTML between <html> and </html> tags.";
            var message = new ContentFile { Type = "text", Text = promptText };
            var claudeRequest = new ClaudeRequestWithFile([message ]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service for HTML conversion");
            }
            
            return ExtractContent(content.Text, "<html>", "</html>");
        }

        private async Task<string> ConvertMarkdownToWordHtmlWithClaude(string markdownContent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                throw new ArgumentException("Markdown content cannot be empty", nameof(markdownContent));
            }

            string promptText = $@"
                Convert this markdown to HTML suitable for Microsoft Word:
                ```markdown
                {markdownContent}
                ```
                Ensure proper headings, lists, code blocks with <pre><code>, tables, and links/images.
                Return ONLY the HTML between <html> and </html> tags.";
            var message = new ContentFile { Type = "text", Text = promptText };
            var claudeRequest = new ClaudeRequestWithFile([ message ]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service for Word HTML conversion");
            }
            
            return ExtractContent(content.Text, "<html>", "</html>");
        }

        private async Task<byte[]> ConvertHtmlToPdfAsync(string html, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html))
                {
                    throw new ArgumentException("HTML content cannot be empty", nameof(html));
                }

                // Configure browser to run completely headless with minimal resources
                var launchOptions = new LaunchOptions 
                { 
                    Headless = true,
                    Args = new[] { 
                        "--no-sandbox", 
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--disable-extensions",
                        "--disable-audio-output",
                        "--disable-web-security",
                        "--mute-audio",
                        "--hide-scrollbars"
                    },
                    IgnoreHTTPSErrors = true,
                    Timeout = 60000
                };
                
                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                using var page = await browser.NewPageAsync();
                
                // Set minimal viewport to reduce memory usage
                await page.SetViewportAsync(new ViewPortOptions { Width = 800, Height = 1100 });
                
                // Set content with basic styling
                await page.SetContentAsync(
                    $"<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><title>PDF Export</title><style>body {{ font-family: Arial, sans-serif; }}</style></head><body>{html}</body></html>");
                
                // Wait for content to be fully rendered
                await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { Timeout = 30000 });
                
                // Generate PDF with minimal settings
                var pdfBytes = await page.PdfDataAsync(new PdfOptions { 
                    Format = PaperFormat.A4, 
                    PrintBackground = true,
                    MarginOptions = new MarginOptions { Top = "1cm", Right = "1cm", Bottom = "1cm", Left = "1cm" }
                });
                
                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    throw new InvalidOperationException("Generated PDF is empty");
                }
                
                return pdfBytes;
            }
            catch (Exception ex)
            {
                await LogErrorAsync("ConvertHtmlToPdfAsync", ex, cancellationToken);
                throw new Exception($"PDF generation failed: {ex.Message}", ex);
            }
            finally
            {
                // No need for additional cleanup as browser disposal should handle process termination
                try
                {
                    // The "using" statements for browser and page already handle proper disposal
                    // No additional cleanup needed as Puppeteer doesn't have a static KillProcessAsync method
                }
                catch
                {
                    // Ignore errors in cleanup
                }
            }
        }

        private async Task<byte[]> ConvertHtmlToWordAsync(string html, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new ArgumentException("HTML content cannot be empty", nameof(html));
            }

            using var package = new MemoryStream();
            using (var wordDocument = WordprocessingDocument.Create(package, WordprocessingDocumentType.Document))
            {
                var mainPart = wordDocument.AddMainDocumentPart();
                mainPart.Document = new Document();
                var body = new Body();
                
                // In a real implementation, parse HTML and create proper Word elements
                // This is a simplified version that just creates a document with the HTML content
                var paragraph = new Paragraph(new Run(new Text(html)));
                body.AppendChild(paragraph);
                mainPart.Document.AppendChild(body);
                
                // Save the document
                mainPart.Document.Save();
            }
            
            await Task.CompletedTask; // For consistency with async signature
            return package.ToArray();
        }

        private async Task<byte[]> FileToByteArrayAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        private decimal CalculateTranslationPrice(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            const decimal pricePerWord = 0.05m; // Example: $0.05 per word
            int wordCount = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return wordCount * pricePerWord;
        }

        private string GetMarkdownTranslateTemplate(string targetLanguage, string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                throw new ArgumentException("Target language cannot be empty", nameof(targetLanguage));
            }

            return $@"
                <markdown_to_translate>
                {markdownContent}
                </markdown_to_translate>
                Translate the markdown into {targetLanguage}, maintaining all formatting (headings, lists, code blocks, etc.).
                Preserve links, images, and structure. Translate only text content, not code or URLs.
                Return the translated markdown between <translation> and </translation> tags.";
        }

        private string GetDocumentToMarkdownTemplate()
        {
            return @"
                Convert the document to Markdown, preserving structure, formatting, and elements like tables and images.
                Use proper Markdown syntax for headings, lists, emphasis, etc.
                Return the converted content between <markdown> and </markdown> tags.";
        }

        private string ExtractContent(string response, string startTag, string endTag)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            var regex = new Regex($"{Regex.Escape(startTag)}(.*?){Regex.Escape(endTag)}", RegexOptions.Singleline);
            var match = regex.Match(response);
            return match.Success ? match.Groups[1].Value.Trim() : response;
        }

        private async Task LogErrorAsync(string methodName, Exception ex, CancellationToken cancellationToken)
        {
            try
            {
                await _requestLogService.Create(new CreateRequestLogModel
                {
                    Request = JsonSerializer.Serialize(new { Method = methodName }),
                    Response = JsonSerializer.Serialize(new { Error = ex.Message, StackTrace = ex.StackTrace }),
                    RequestType = RequestType.Error,
                }, cancellationToken);
            }
            catch (Exception logEx)
            {
                Console.WriteLine($"Error logging exception: {logEx.Message}");
            }
        }
        #endregion
    }
}
