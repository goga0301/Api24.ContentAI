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

        public DocumentTranslationService(
            IClaudeService claudeService,
            ILanguageService languageService,
            IUserRepository userRepository,
            IRequestLogService requestLogService,
            IGptService gptService)
        {
            _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
            _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _requestLogService = requestLogService ?? throw new ArgumentNullException(nameof(requestLogService));
            _gptService = gptService ?? throw new ArgumentNullException(nameof(gptService));
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
                    Console.WriteLine($"Note: Requested format was {outputFormat}, but returning Markdown due to conversion limitations");
                    translationResult.OutputFormat = Domain.Models.DocumentFormat.Markdown;
                    translationResult.FileData = Encoding.UTF8.GetBytes(translationResult.TranslatedContent);
                    translationResult.FileName = $"translated_{translationResult.TranslationId}.md";
                    translationResult.ContentType = "text/markdown";
                }

                return translationResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in TranslateDocument: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
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
                        fileContent = await ExtractTextFromPdfAsync(file, cancellationToken);
                        break;
                    case ".docx":
                    case ".doc":
                        fileContent = await ExtractTextFromWordAsync(file, cancellationToken);
                        break;
                    case ".txt":
                    case ".md":
                        // For text files, just read the content directly
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
                    return new DocumentConversionResult { Success = false, ErrorMessage = "No content could be extracted from the file" };
                }

                string markdownContent;
                if (fileExtension == ".pdf" || fileExtension == ".docx" || fileExtension == ".doc")
                {
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
                Console.WriteLine($"Error in ConvertToMarkdown: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
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

                Console.WriteLine("Starting translation verification with GPT service...");
                VerificationResult verificationResult = await _gptService.VerifyTranslationBatch(
                    translationResults.ToList(), cancellationToken);
                Console.WriteLine($"Translation verification completed. Success: {verificationResult.Success}, Score: {verificationResult.QualityScore}");

                string translationId = Guid.NewGuid().ToString();
                
                Console.WriteLine($"Translation completed: ID={translationId}, TargetLanguage={language.Name}, ChunkCount={chunks.Count}, Length={translatedMarkdown.Length}");

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
                Console.WriteLine($"Error in TranslateMarkdown: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
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
                
                Console.WriteLine($"Note: Returning markdown format regardless of requested format ({outputFormat})");

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
                Console.WriteLine($"Error in ConvertFromMarkdown: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
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
                Console.WriteLine($"Error extracting text with OpenXml: {ex.Message}");
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
            var message = new ContentFile 
            { 
                Type = "text", 
                Text = $"Convert the following text to well-formatted Markdown:\n\n{textContent}\n\nReturn the converted content between <markdown> and </markdown> tags." 
            };

            var claudeRequest = new ClaudeRequestWithFile([message]);
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

            Console.WriteLine($"Translating chunk {order}:");
            Console.WriteLine(markdownChunk.Substring(0, Math.Min(100, markdownChunk.Length)) + "...");

            string promptText = $@"
                     Translate the following markdown content into {targetLanguage}. 
                     Preserve all markdown formatting, headings, lists, and code blocks.
                     Only translate the text content, not code or URLs.
                     ```markdown
                        {markdownChunk}
                     ```

                      Return ONLY the translated markdown between <translation> and </translation> tags.";

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
