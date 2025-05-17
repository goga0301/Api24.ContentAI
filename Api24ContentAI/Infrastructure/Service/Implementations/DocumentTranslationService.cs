using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class DocumentTranslationService : IDocumentTranslationService
    {
        private readonly IClaudeService _claudeService;
        private readonly ILanguageService _languageService;
        private readonly IUserRepository _userRepository;
        private readonly IRequestLogService _requestLogService;
        private readonly IGptService _gptService;
        private readonly ILogger<DocumentTranslationService> _logger;

        public DocumentTranslationService(
            IClaudeService claudeService,
            ILanguageService languageService,
            IUserRepository userRepository,
            IRequestLogService requestLogService,
            IGptService gptService,
            ILogger<DocumentTranslationService> logger)
        {
            _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _requestLogService = requestLogService ?? throw new ArgumentNullException(nameof(requestLogService));
            _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

                DocumentTranslationResult translationResult = await TranslateMarkdown(conversionResult.Content, targetLanguageId, userId, cancellationToken);
                if (!translationResult.Success)
                    return translationResult;

                // Always return markdown format for now
                if (outputFormat != Domain.Models.DocumentFormat.Markdown)
                {
                    _logger.LogInformation("Note: Requested format was {OutputFormat}, but returning Markdown due to conversion limitations", outputFormat);
                    translationResult.OutputFormat = Domain.Models.DocumentFormat.Markdown;
                    translationResult.FileData = Encoding.UTF8.GetBytes(translationResult.TranslatedContent);
                    translationResult.FileName = $"translated_{translationResult.TranslationId}.md";
                    translationResult.ContentType = "text/markdown";
                }

                return translationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TranslateDocument");
                
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error in document translation workflow: {ex.Message}" };
            }
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
                        _logger.LogInformation("Processing PDF file: {FileName}, size: {Length} bytes", 
                            file.FileName, file.Length);
                        fileContent = await ExtractTextFromPdfAsync(file, cancellationToken);
                        break;
                    case ".docx":
                    case ".doc":
                        _logger.LogInformation("Processing Word document: {FileName}, size: {Length} bytes", 
                            file.FileName, file.Length);
                        fileContent = await ExtractTextFromWordAsync(file, cancellationToken);
                        break;
                    case ".txt":
                    case ".md":
                        // For text files, just read the content directly
                        _logger.LogInformation("Processing text file: {FileName}, size: {Length} bytes", 
                            file.FileName, file.Length);
                        using (var reader = new StreamReader(file.OpenReadStream()))
                        {
                            fileContent = await reader.ReadToEndAsync();
                        }
                        break;
                    default:
                        return new DocumentConversionResult { Success = false, ErrorMessage = "Unsupported file format. Please provide a PDF, Word document, or text file." };
                }

                if (string.IsNullOrWhiteSpace(fileContent))
                {
                    _logger.LogWarning("No content could be extracted from file: {FileName}", file.FileName);
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No content could be extracted from the file" };
                }

                _logger.LogInformation("Successfully extracted {Length} characters from {FileName}", 
                    fileContent.Length, file.FileName);

                string markdownContent;
                if (fileExtension == ".pdf" || fileExtension == ".docx" || fileExtension == ".doc")
                {
                    _logger.LogInformation("Converting extracted content to Markdown");
                    markdownContent = await ConvertTextToMarkdownWithClaude(fileContent, cancellationToken);
                }
                else
                {
                    markdownContent = fileContent;
                }

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
                _logger.LogError(ex, "Error in ConvertToMarkdown for file: {FileName}", file?.FileName);
                
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

                _logger.LogInformation("Starting translation verification with GPT service...");
                VerificationResult verificationResult = await _gptService.VerifyTranslationBatch(
                    translationResults.ToList(), cancellationToken);
                _logger.LogInformation("Translation verification completed. Success: {Success}, Score: {Score}", 
                    verificationResult.Success, verificationResult.QualityScore);

                string translationId = Guid.NewGuid().ToString();
                
                _logger.LogInformation("Translation completed: ID={TranslationId}, TargetLanguage={TargetLanguage}, ChunkCount={ChunkCount}, Length={Length}", 
                    translationId, language.Name, chunks.Count, translatedMarkdown.Length);

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
                _logger.LogError(ex, "Error in TranslateMarkdown");
                
                return new DocumentTranslationResult { Success = false, ErrorMessage = $"Error translating markdown: {ex.Message}" };
            }
        }

        
        public async Task<DocumentConversionResult> ConvertFromMarkdown(string markdownContent, Domain.Models.DocumentFormat outputFormat, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdownContent))
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No markdown content provided" };

                byte[] fileData = Encoding.UTF8.GetBytes(markdownContent);
                string fileName = $"document_{Guid.NewGuid()}.md";
                string contentType = "text/markdown";
                
                _logger.LogInformation("Note: Returning markdown format regardless of requested format ({OutputFormat})", outputFormat);

                return new DocumentConversionResult
                {
                    Success = true,
                    FileData = fileData,
                    OutputFormat = Domain.Models.DocumentFormat.Markdown,
                    FileName = fileName,
                    ContentType = contentType
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ConvertFromMarkdown");
                
                return new DocumentConversionResult { Success = false, ErrorMessage = $"Error converting from markdown: {ex.Message}" };
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
            bool hasExtractableText = false;
            bool mightContainImages = false;

            // First try standard text extraction
            using (var stream = new MemoryStream(bytes))
            using (var pdfReader = new PdfReader(stream))
            using (var pdf = new PdfDocument(pdfReader))
            {
                int pageCount = pdf.GetNumberOfPages();
                _logger.LogInformation("Processing PDF with {PageCount} pages", pageCount);
                
                for (int i = 1; i <= pageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = pdf.GetPage(i);
                    
                    // Check if page might contain images
                    var resources = page.GetResources();
                    if (resources != null && resources.GetResourceNames(PdfName.XObject).Count > 0)
                    {
                        mightContainImages = true;
                    }

                    string text = PdfTextExtractor.GetTextFromPage(page);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        hasExtractableText = true;
                        builder.AppendLine(text);
                    }
                }
            }

            string extractedText = builder.ToString();

            if (mightContainImages && (!hasExtractableText || extractedText.Length < 100))
            {
                _logger.LogInformation("PDF appears to be image-based or has little text. Using Claude for OCR processing");
                string ocrText = await ExtractTextFromPdfWithClaudeAsync(file, bytes, cancellationToken);
                
                // If we got text from both methods, combine them
                if (hasExtractableText && !string.IsNullOrWhiteSpace(ocrText))
                {
                    _logger.LogInformation("Combining extracted text with OCR results");
                    return $"{extractedText}\n\n{ocrText}";
                }
                
                return ocrText;
            }
            
            return extractedText;
        }

        private async Task<string> ExtractTextFromPdfWithClaudeAsync(IFormFile file, byte[] pdfBytes, CancellationToken cancellationToken)
        {
            try
            {
                string base64Pdf = Convert.ToBase64String(pdfBytes);
                
                var message = new ContentFile 
                { 
                    Type = "text", 
                    Text = $@"This PDF document contains images that may contain text. Please:
                        1. Perform OCR on any images in the document to extract text
                        2. Extract all readable text content from the document
                        3. Combine and organize all extracted text to maintain document flow
                        4. Return the complete extracted content preserving structure

                File name: {file.FileName}
                Content type: {file.ContentType}
                Base64 content: {base64Pdf.Substring(0, Math.Min(base64Pdf.Length, 100))}...[truncated]"
                };

                var claudeRequest = new ClaudeRequestWithFile([message]);
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        
                var content = claudeResponse.Content?.SingleOrDefault();
                if (content == null)
                {
                    throw new InvalidOperationException("No content received from Claude service");
                }
        
                _logger.LogInformation("Successfully extracted text using OCR, response length: {Length}", 
                    content.Text?.Length ?? 0);
        
                return content.Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF with OCR");
                throw;
            }
        }

        private async Task<string> ExtractTextFromPdfWithImageProcessingAsync(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                byte[] pdfBytes = await FileToByteArrayAsync(file, cancellationToken);
                StringBuilder textFromPdf = new();
                
                // Process each page as an image
                using (var stream = new MemoryStream(pdfBytes))
                using (var pdfReader = new PdfReader(stream))
                using (var pdf = new PdfDocument(pdfReader))
                {
                    int pageCount = pdf.GetNumberOfPages();
                    _logger.LogInformation("Processing {PageCount} PDF pages as images", pageCount);
                    
                    List<Task<KeyValuePair<int, string>>> pageTasks = new();
                    
                    // Process up to 5 pages in parallel to avoid overwhelming the service
                    for (int pageNum = 1; pageNum <= pageCount; pageNum++)
                    {
                        int currentPage = pageNum;
                        Task<KeyValuePair<int, string>> task = Task.Run(async () => {
                            try
                            {
                                // Convert PDF page to image (this would require a PDF rendering library)
                                // For now, we'll use Claude directly with the PDF
                                
                                string base64Pdf = Convert.ToBase64String(pdfBytes);
                                
                                ContentFile fileMessage = new()
                                {
                                    Type = "text",
                                    Text = $"I'm sending you a PDF document. Please extract all text from page {currentPage} only. Return just the extracted text with no explanations.\n\nFile: {file.FileName}\nBase64: {base64Pdf.Substring(0, Math.Min(100, base64Pdf.Length))}...[truncated]"
                                };
                                
                                ClaudeRequestWithFile claudeRequest = new([fileMessage]);
                                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                                string extractedText = claudeResponse.Content.Single().Text;
                                
                                _logger.LogDebug("Extracted {Length} characters from page {Page}", 
                                    extractedText?.Length ?? 0, currentPage);
                                
                                return new KeyValuePair<int, string>(currentPage, extractedText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing PDF page {Page}", currentPage);
                                return new KeyValuePair<int, string>(currentPage, string.Empty);
                            }
                        }, cancellationToken);
                        
                        pageTasks.Add(task);
                        
                        // Process in batches of 5 pages
                        if (pageTasks.Count >= 5 || pageNum == pageCount)
                        {
                            KeyValuePair<int, string>[] results = await Task.WhenAll(pageTasks);
                            
                            foreach (var result in results.OrderBy(r => r.Key))
                            {
                                if (!string.IsNullOrWhiteSpace(result.Value))
                                {
                                    textFromPdf.AppendLine(result.Value);
                                    textFromPdf.AppendLine();
                                }
                            }
                            
                            pageTasks.Clear();
                        }
                    }
                }
                
                return textFromPdf.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF with image processing");
                throw;
            }
        }

        private async Task<string> ExtractTextFromWordAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            
            byte[] fileBytes = await FileToByteArrayAsync(file, cancellationToken);
            
            try
            {
                using var memoryStream = new MemoryStream(fileBytes);
                using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(memoryStream, false);
                var body = wordDoc.MainDocumentPart.Document.Body;
                
                if (body != null)
                {
                    return body.InnerText;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting text with OpenXml");
            }
            
            string base64File = Convert.ToBase64String(fileBytes);
            
            var message = new ContentFile 
            { 
                Type = "text", 
                Text = $"I'm sending you a Word document as a base64 string. Please extract all the text content from it, preserving paragraphs and structure as much as possible.\n\nFile name: {file.FileName}\nContent type: {file.ContentType}\nBase64 content: {base64File.Substring(0, Math.Min(base64File.Length, 100))}...[truncated]" 
            };

            var claudeRequest = new ClaudeRequestWithFile([message]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service");
            }
            
            return content.Text;
        }

        private async Task<string> ConvertTextToMarkdownWithClaude(string textContent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(textContent))
            {
                throw new ArgumentException("Text content cannot be empty", nameof(textContent));
            }

            _logger.LogDebug("Converting {Length} characters to Markdown", textContent.Length);
            
            // If the content is very large, we need to chunk it
            if (textContent.Length > 10000)
            {
                _logger.LogInformation("Content is large ({Length} chars), processing in chunks", textContent.Length);
                return await ConvertLargeTextToMarkdownWithClaude(textContent, cancellationToken);
            }

            var message = new ContentFile 
            { 
                Type = "text", 
                Text = $@"Convert the following text to well-formatted Markdown:

                    ```
                    {textContent}
                    ```

                    Guidelines:
                    1. Preserve the document structure (headings, paragraphs, lists)
                    2. Use proper Markdown syntax for formatting
                    3. Identify and format headings with appropriate # levels
                    4. Convert lists to proper Markdown lists
                    5. Preserve any tables using Markdown table syntax
                    6. Return ONLY the converted Markdown between <markdown> and </markdown> tags"
            };

            var claudeRequest = new ClaudeRequestWithFile([message]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service");
            }
            
            string markdownContent = ExtractContent(content.Text, "<markdown>", "</markdown>");
            
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                markdownContent = ExtractContent(content.Text, "```markdown", "```");
                
                if (string.IsNullOrWhiteSpace(markdownContent))
                {
                    markdownContent = content.Text.Trim();
                }
            }
            
            _logger.LogInformation("Successfully converted text to Markdown, result length: {Length}", 
                markdownContent.Length);
            
            return markdownContent;
        }

        private async Task<string> ConvertLargeTextToMarkdownWithClaude(string textContent, CancellationToken cancellationToken)
        {
            List<string> chunks = SplitByParagraphs(textContent, 8000);
            _logger.LogInformation("Split large content into {ChunkCount} chunks", chunks.Count);
            
            List<Task<KeyValuePair<int, string>>> tasks = new();
            
            for (int i = 0; i < chunks.Count; i++)
            {
                int chunkIndex = i;
                string chunk = chunks[i];
                
                Task<KeyValuePair<int, string>> task = Task.Run(async () => {
                    try
                    {
                        var message = new ContentFile 
                        { 
                            Type = "text", 
                            Text = $@"Convert the following text (chunk {chunkIndex + 1} of {chunks.Count}) to well-formatted Markdown:

                            ```
                            {chunk}
                            ```

                            Guidelines:
                            1. Preserve the document structure (headings, paragraphs, lists)
                            2. Use proper Markdown syntax for formatting
                            3. Identify and format headings with appropriate # levels
                            4. Convert lists to proper Markdown lists
                            5. Preserve any tables using Markdown table syntax
                            6. Return ONLY the converted Markdown between <markdown> and </markdown> tags"
                        };

                        var claudeRequest = new ClaudeRequestWithFile([message]);
                        var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                        
                        var content = claudeResponse.Content?.SingleOrDefault();
                        if (content == null)
                        {
                            throw new InvalidOperationException("No content received from Claude service");
                        }
                        
                        string markdownContent = ExtractContent(content.Text, "<markdown>", "</markdown>");
                        
                        // If we couldn't find the markdown tags, try to extract from code blocks
                        if (string.IsNullOrWhiteSpace(markdownContent))
                        {
                            markdownContent = ExtractContent(content.Text, "```markdown", "```");
                            
                            // If still empty, just use the whole response
                            if (string.IsNullOrWhiteSpace(markdownContent))
                            {
                                markdownContent = content.Text.Trim();
                            }
                        }
                        
                        _logger.LogDebug("Successfully converted chunk {Index} to Markdown, result length: {Length}", 
                            chunkIndex, markdownContent.Length);
                        
                        return new KeyValuePair<int, string>(chunkIndex, markdownContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error converting chunk {Index} to Markdown", chunkIndex);
                        return new KeyValuePair<int, string>(chunkIndex, chunk); // Return original chunk on error
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }
            
            var results = await Task.WhenAll(tasks);
            
            // Combine the chunks in the correct order
            StringBuilder combinedMarkdown = new();
            foreach (var result in results.OrderBy(r => r.Key))
            {
                combinedMarkdown.AppendLine(result.Value);
                combinedMarkdown.AppendLine();
            }
            
            return combinedMarkdown.ToString();
        }

        private List<string> GetChunksOfMarkdown(string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new List<string>();
            }

            int maxChunkSize = 8000;
            
            if (markdownContent.Length <= maxChunkSize)
            {
                return new List<string> { markdownContent };
            }
            
            return SplitByParagraphs(markdownContent, maxChunkSize);
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
                currentChunk.AppendLine(); 
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

            _logger.LogDebug("Translating chunk {Order}: {Preview}", order, 
                markdownChunk.Substring(0, Math.Min(100, markdownChunk.Length)) + "...");

            string promptText = GetMarkdownChunkTranslatePrompt(targetLanguage, markdownChunk);
            var message = new ContentFile { Type = "text", Text = promptText };
            var claudeRequest = new ClaudeRequestWithFile([message]);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            var content = claudeResponse.Content?.SingleOrDefault();
            if (content == null)
            {
                throw new InvalidOperationException("No content received from Claude service for translation");
            }
            
            string translatedChunk = ExtractContent(content.Text, "<translation>", "</translation>");
            
            if (string.IsNullOrWhiteSpace(translatedChunk))
            {
                translatedChunk = content.Text.Trim();
                
                translatedChunk = translatedChunk.Replace("```markdown", "").Replace("```", "").Trim();
            }
            
            return new KeyValuePair<int, string>(order, translatedChunk);
        }

        private async Task<(byte[] fileData, string contentType, string fileName)> ConvertMarkdownToPdfAsync(string markdownContent, CancellationToken cancellationToken)
        {
            byte[] markdownBytes = Encoding.UTF8.GetBytes(markdownContent);
            return (markdownBytes, "text/markdown", $"document_{Guid.NewGuid()}.md");
        }

        private async Task<(byte[] fileData, string contentType, string fileName)> ConvertMarkdownToWordAsync(string markdownContent, CancellationToken cancellationToken)
        {
            byte[] markdownBytes = Encoding.UTF8.GetBytes(markdownContent);
            return (markdownBytes, "text/markdown", $"document_{Guid.NewGuid()}.md");
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

        private string GetMarkdownChunkTranslatePrompt(string targetLanguage, string markdownChunk)
        {
            return $@"
                Imagine you're an advanced AI language model with a deep understanding of both markdown syntax and languages. Your task is to translate the following markdown content into {targetLanguage}, while maintaining all of its structure and formatting.

                The markdown content you will translate contains a combination of text, headings, lists, code blocks, and URLs. It's crucial that you follow these guidelines during the translation process:

                1. **Text Translation:**
                - Only the **text content** (paragraphs, headings, list items, etc.) should be translated into {targetLanguage}.
                - Keep in mind the grammatical nuances and idiomatic expressions of the target language to ensure the translation feels natural and accurate.

                2. **Preserving Markdown Formatting:**
                - **Do not change any markdown syntax** such as headings, lists, blockquotes, or links.
                - **Maintain code blocks, inline code, and URLs exactly as they are**; do not translate or alter them in any way. This ensures the code and external links remain functional and correct.

                3. **Handling Special Cases:**
                - **Code blocks:** These should remain untouched, including syntax highlighting if present. Treat them purely as non-translatable content.
                - **URLs and links:** Any URLs or hyperlinks should remain unchanged. Their paths, text, and structure must remain intact, as they are not to be translated.

                4. **Return Translated Markdown:**
                - Once you have translated the text, **return only the translated markdown** content between `<translation>` and `</translation>` tags. This allows the translation to be easily extracted while keeping the original markdown structure intact.

                 Remember: The goal is to provide a clear and accurate translation of the text while keeping all markdown elements, code, and URLs unchanged.
                Be sure to preserve the original formatting, as this is a key part of the task.

                Here is the markdown content that you need to translate into {targetLanguage}:
                {markdownChunk}
               ";
        
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
        #endregion
    }
}