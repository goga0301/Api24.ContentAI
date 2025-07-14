using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Unicode;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Api24ContentAI.Domain.Repository;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using System.Text.RegularExpressions;
using SelectPdf;
using System.Net.Http;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class UserContentService : IUserContentService
    {
        private readonly IClaudeService _claudeService;
        private readonly IUserRequestLogService _requestLogService;
        private readonly IProductCategoryService _productCategoryService;
        private readonly ILanguageService _languageService;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<UserContentService> _logger;

        public UserContentService(
            IClaudeService claudeService,
            IUserRequestLogService requestLogService,
            IProductCategoryService productCategoryService,
            ILanguageService languageService,
            IUserRepository userRepository,
            ILogger<UserContentService> logger)
        {
            _claudeService = claudeService;
            _requestLogService = requestLogService;
            _productCategoryService = productCategoryService;
            _languageService = languageService;
            _userRepository = userRepository;
            _logger = logger;
        }

        private static readonly string[] SupportedFileExtensions =
        [
            "jpeg",
            "png",
            "gif",
            "webp"
        ];

        public async Task<CopyrightAIResponse> BasicMessage(BasicMessageRequest request,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting BasicMessage request");

            ContentFile message = new()
            {
                Type = "text",
                Text = request.Message
            };

            ClaudeRequestWithFile claudeRequest = new([message]);
            
            try
            {
                _logger.LogInformation("Sending BasicMessage request to Claude API. Message length: {MessageLength}", request.Message?.Length ?? 0);
                
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for BasicMessage");
                
                var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

                var lastPeriod = claudResponseText.LastIndexOf('.');

                if (lastPeriod != -1)
                {
                    claudResponseText = new string([.. claudResponseText.Take(lastPeriod + 1)]);
                }

                CopyrightAIResponse response = new()
                {
                    Text = claudResponseText
                };

                _logger.LogInformation("BasicMessage request completed successfully. Response length: {ResponseLength}", response.Text?.Length ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for BasicMessage. Request: {Request}", 
                    JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process BasicMessage request: {ex.Message}", ex);
            }
        }

        public async Task<CopyrightAIResponse> CopyrightAI(IFormFile file, UserCopyrightAIRequest request, string userId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting CopyrightAI request for user {UserId}", userId);
            
            var requestPrice = GetRequestPrice(RequestType.Copyright);
            var user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for CopyrightAI request. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                    userId, user.UserBalance.Balance, requestPrice);
                throw new Exception("CopyrightAI რექვესთების ბალანსი ამოიწურა");
            }

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);

            var templateText = GetCopyrightTemplate(language.Name, request.ProductName);

            ContentFile message = new()
            {
                Type = "text",
                Text = templateText
            };

            var extension = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported file extension for CopyrightAI: {Extension}. File: {FileName}", extension, file.FileName);
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            ContentFile fileMessage = new()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extension}",
                    Data = EncodeFileToBase64(file)
                }
            };

            ClaudeRequestWithFile claudeRequest = new([fileMessage, message]);
            
            try
            {
                _logger.LogInformation("Sending CopyrightAI request to Claude API. User: {UserId}, Language: {Language}, File: {FileName}", 
                    userId, language.Name, file.FileName);
                    
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for CopyrightAI. User: {UserId}", userId);
                
                string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

                int lastPeriod = claudResponseText.LastIndexOf('.');

                if (lastPeriod != -1)
                {
                    claudResponseText = new string([.. claudResponseText.Take(lastPeriod + 1)]);
                }

                CopyrightAIResponse response = new()
                {
                    Text = claudResponseText
                };

                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = RequestType.Copyright
                }, cancellationToken);

                await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);

                _logger.LogInformation("CopyrightAI request completed successfully. User: {UserId}, Response length: {ResponseLength}", 
                    userId, response.Text?.Length ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for CopyrightAI. User: {UserId}, Request: {Request}", 
                    userId, JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process CopyrightAI request: {ex.Message}", ex);
            }
        }

        public async Task<ContentAIResponse> SendRequest(UserContentAIRequest request, string userId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting ContentAI request for user {UserId}", userId);
            
            decimal requestPrice = GetRequestPrice(RequestType.Copyright);

            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for ContentAI request. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                    userId, user.UserBalance.Balance, requestPrice);
                throw new Exception("ContentAI რექვესთების ბალანსი ამოიწურა");
            }

            ProductCategoryModel productCategory =
                await _productCategoryService.GetById(request.ProductCategoryId, cancellationToken);

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

            string templateText = GetDefaultTemplate(productCategory.NameEng, language.Name);

            string claudRequestContent =
                $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

            ClaudeRequest claudeRequest = new(claudRequestContent);

            try
            {
                _logger.LogInformation("Sending ContentAI request to Claude API. User: {UserId}, ProductCategory: {Category}, Language: {Language}, Content length: {ContentLength}", 
                    userId, productCategory.NameEng, language.Name, claudRequestContent.Length);
                    
                ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for ContentAI. User: {UserId}", userId);
                
                string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
                int lastPeriod = claudResponseText.LastIndexOf('.');

                if (lastPeriod != -1)
                {
                    claudResponseText = new string([.. claudResponseText.Take(lastPeriod + 1)]);
                }

                ContentAIResponse response = new()
                {
                    Text = claudResponseText
                };

                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = RequestType.Content

                }, cancellationToken);

                await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);

                _logger.LogInformation("ContentAI request completed successfully. User: {UserId}, Response length: {ResponseLength}", 
                    userId, response.Text?.Length ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for ContentAI. User: {UserId}, Request: {Request}", 
                    userId, JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process ContentAI request: {ex.Message}", ex);
            }
        }

        public async Task<string> TestTranslateTextAsync(IFormFile file, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting TestTranslateTextAsync request. File: {FileName}", file.FileName);

            var templateText = GetDocumentToMarkdownTemplate1(file.FileName);
            var contents = new List<ContentFile>();

            var messageFile = new ContentFile()
            {
                Type = "document",
                Source = new Source
                {
                    Type = "base64",
                    MediaType = "application/pdf",
                    Data = EncodeFileToBase64(file),
                },
            };

            var message = new ContentFile
            {
                Type = "text",
                Text = templateText,
            };
            contents.Add(messageFile);
            contents.Add(message);

            string systemPrompt = GetSystemPromptForPdfToHtml();
            var claudeRequest = new ClaudeRequestWithFile(contents, systemPrompt);
            
            try
            {
                _logger.LogInformation("Sending TestTranslateTextAsync request to Claude API. File: {FileName}, Size: {FileSize} bytes", 
                    file.FileName, file.Length);
                    
                var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for TestTranslateTextAsync. File: {FileName}", file.FileName);
                
                var claudResponsePlainText = claudeResponse.Content.Single().Text;

                var start = claudResponsePlainText.IndexOf("<html_output>", StringComparison.Ordinal) + 13;
                var end = claudResponsePlainText.IndexOf("</html_output>", StringComparison.Ordinal);

                var result = claudResponsePlainText.Substring(start, end - start);
                
                _logger.LogInformation("TestTranslateTextAsync request completed successfully. File: {FileName}, Result length: {ResultLength}", 
                    file.FileName, result.Length);
                    
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for TestTranslateTextAsync. File: {FileName}", file.FileName);
                throw new Exception($"Failed to process TestTranslateTextAsync request: {ex.Message}", ex);
            }
        }

        public async Task<TranslateResponse> ChunkedTranslate(UserTranslateRequestWithChunks request, string userId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting translation request for user {UserId}", userId);
            
            if (string.IsNullOrWhiteSpace(request.UserText))
            {
                _logger.LogWarning("No text provided for translation");
                throw new Exception("No text to translate. Please provide text in the description field.");
            }
            
            var user = await _userRepository.GetById(userId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                throw new Exception("User not found");
            }
            
            string textToTranslate = request.UserText;
            
            decimal requestPrice = GetRequestPrice(RequestType.Translate) * 
                                   ((textToTranslate.Length / 250) + (textToTranslate.Length % 250 == 0 ? 0 : 1));
            
            if (user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for user {UserId}. Required: {Price}, Available: {Balance}", 
                    userId, requestPrice, user.UserBalance.Balance);
                throw new Exception("ბალანსი არ არის საკმარისი მოთხოვნის დასამუშავებლად!!!");
            }
            
            var targetLanguage = await _languageService.GetById(request.LanguageId, cancellationToken);
            var sourceLanguage = await _languageService.GetById(request.SourceLanguageId, cancellationToken);
            
            _logger.LogInformation("Translating {Length} characters from {SourceLanguage} to {TargetLanguage}", 
                textToTranslate.Length, sourceLanguage.Name, targetLanguage.Name);
            
            var chunks = GetChunksOfLargeText(textToTranslate);
            StringBuilder translatedText = new();
            List<Task<KeyValuePair<int, string>>> tasks = [];
            
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                _logger.LogDebug("Creating translation task for chunk {Index}, length: {Length} chars", i, chunk.Length);
                var task = TranslateTextAsync(i, chunk, targetLanguage.Name, sourceLanguage.Name, cancellationToken);
                tasks.Add(task);
            }
            
            _logger.LogInformation("Waiting for {Count} translation tasks to complete", tasks.Count);
            var results = await Task.WhenAll(tasks);
            
            foreach (var result in results.OrderBy(r => r.Key))
            {
                translatedText.AppendLine(result.Value);
            }
            
            string finalTranslation = translatedText.ToString();
            _logger.LogInformation("Translation completed, final length: {Length} characters", finalTranslation.Length);
            
            TranslateResponse response = new()
            {
                Text = finalTranslation
            };
            
            
            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }),
                RequestType = RequestType.Translate,
            }, cancellationToken);
            
            await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);
            
            return response;
        }

        private static byte[] ConvertHtmlToPdf(StringBuilder htmlContent)
        {
            var converter = new HtmlToPdf
            {
                Options =
                {
                    PdfPageSize = PdfPageSize.A4,
                    PdfPageOrientation = PdfPageOrientation.Portrait,
                    MarginLeft = 10,
                    MarginRight = 10,
                    MarginTop = 10,
                    MarginBottom = 10
                }
            };
            var pdfDocument = converter.ConvertHtmlString(htmlContent.ToString());
            try
            {
                byte[] pdfBytes;
                using (var ms = new MemoryStream())
                {
                    pdfDocument.Save(ms);
                    pdfBytes = ms.ToArray();
                }

                pdfDocument.Close();
                return pdfBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PDF conversion error: {ex.Message}");
                return [];
            }
        }

        public async Task<EnhanceTranslateResponse> EnhanceTranslate(UserTranslateEnhanceRequest request, string userId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting EnhanceTranslate request for user {UserId}", userId);
            
            decimal requestPrice = GetRequestPrice(RequestType.EnhanceTranslate);
            User user = await _userRepository.GetById(userId, cancellationToken);
    
            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for EnhanceTranslate request. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                    userId, user.UserBalance.Balance, requestPrice);
                throw new Exception("EnhanceTranslate მოთხოვნისთვის არასაკმარისი ბალანსია");
            }
    
            LanguageModel targetLanguage = await _languageService.GetById(request.TargetLanguageId, cancellationToken);
            string templateText = GetEnhanceTranslateTemplate(targetLanguage.Name, request.UserInput, request.TranslateOutput);
    
            // Create content for Claude request
            List<ContentFile> contents = new List<ContentFile>();
            ContentFile message = new ContentFile
            {
                Type = "text",
                Text = templateText
            };
            contents.Add(message);
    
            ClaudeRequestWithFile claudeRequest = new ClaudeRequestWithFile(contents);
            
            try
            {
                _logger.LogInformation("Sending EnhanceTranslate request to Claude API. User: {UserId}, TargetLanguage: {Language}, InputLength: {InputLength}, TranslateOutputLength: {OutputLength}", 
                    userId, targetLanguage.Name, request.UserInput?.Length ?? 0, request.TranslateOutput?.Length ?? 0);
                    
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for EnhanceTranslate. User: {UserId}", userId);
    
                string claudeResponsePlainText = claudeResponse.Content.Single().Text;
    
                // Parse Claude's response to extract enhanced text and suggestions
                // Assuming Claude returns structured response with enhanced text and explanations
                var parsedResponse = ParseClaudeEnhanceResponse(claudeResponsePlainText);
    
                // Create the correct response type
                EnhanceTranslateResponse response = new EnhanceTranslateResponse
                {
                    OriginalText = request.TranslateOutput,
                    EnhancedText = parsedResponse.EnhancedText.Replace("\n", "<br>"),
                    Suggestion = parsedResponse.Suggestions,
                    ChangeCount = parsedResponse.Suggestions.Count
                };
    
                // Log the request
                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(request, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(response, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = RequestType.EnhanceTranslate
                }, cancellationToken);
    
                // Update user balance
                await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);
    
                _logger.LogInformation("EnhanceTranslate request completed successfully. User: {UserId}, EnhancedTextLength: {EnhancedLength}, SuggestionCount: {SuggestionCount}", 
                    userId, response.EnhancedText?.Length ?? 0, response.ChangeCount);
                    
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for EnhanceTranslate. User: {UserId}, Request: {Request}", 
                    userId, JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process EnhanceTranslate request: {ex.Message}", ex);
            }
        }

        // Helper method to parse Claude's response
        private (string EnhancedText, List<string> Suggestions) ParseClaudeEnhanceResponse(string claudeResponse)
        {
            string enhancedText = "";
            List<string> suggestions = new List<string>();
    
            // Extract content between <enhanced_text> tags
            var enhancedTextMatch = System.Text.RegularExpressions.Regex.Match(
                claudeResponse, 
                @"<enhanced_text>(.*?)</enhanced_text>", 
                System.Text.RegularExpressions.RegexOptions.Singleline
            );
    
            if (enhancedTextMatch.Success)
            {
                enhancedText = enhancedTextMatch.Groups[1].Value
                    .Replace("<br>", "")
                    .Replace("</br>", "")
                    .Trim();
            }
    
            // Extract content between <suggestions> tags
            var suggestionsMatch = System.Text.RegularExpressions.Regex.Match(
                claudeResponse, 
                @"<suggestions>(.*?)</suggestions>", 
                System.Text.RegularExpressions.RegexOptions.Singleline
            );
    
            if (suggestionsMatch.Success)
            {
                string suggestionsContent = suggestionsMatch.Groups[1].Value;
        
                // Split by <br> and clean up each suggestion
                var suggestionLines = suggestionsContent
                    .Split(new[] { "<br>", "</br>" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            
                suggestions = suggestionLines;
            }
    
            return (enhancedText, suggestions);
        }        private string GetSystemPromptForPdfToHtml()
        {
            string systemPrompt = @"You are an AI assistant specialized in converting Word and PDF documents to HTML format. You maintain the original document's structure, formatting, and visual elements while ensuring compatibility with A4 page size (210mm x 297mm).

                Your conversion process follows these steps:

                1. Analyze the input file to determine its type (Word/PDF) and content structure
                2. Extract and preserve formatted text (headings, paragraphs, lists) with proper HTML tags
                3. Convert tables to HTML table format
                4. Handle images by extracting them and using appropriate <img> tags
                5. Implement proper HTML hierarchy (<h1>, <h2>, <p>, <ul>, etc.)
                6. Format footnotes/endnotes with <sup> and <a> tags
                7. Add CSS styling for A4 compatibility including:
                - Page dimensions (210mm x 297mm)
                - Appropriate margins and padding
                - Print media queries
                - Page breaks and layout preservation
                8. Generate a complete HTML file with proper syntax
                9. Ensure all content from the original is preserved in the output

                When responding, provide only the converted HTML content within <html_output> tags without any explanations or comments outside those tags.

                Example response format:
                <html_output>
                <!DOCTYPE html>
                <html>
                <head>
                <title>Converted Document</title>
                <style>
                /* A4 formatting CSS here */
                </style>
                </head>
                <body>
                <!-- Converted content here -->
                </body>
                </html>
                </html_output>";

            return systemPrompt;

        }

        private decimal CalculateTranslateRequestPriceNew(string description, bool isPdf, int pageCount)
            //private static decimal CalculateTranslateRequestPriceNew(string description)
        {
            var defaultPrice = GetRequestPrice(RequestType.Translate);
            if (isPdf)
            {
                return defaultPrice * pageCount;
            }
            return defaultPrice * ((description.Length / 250) + description.Length % 250 == 0 ? 0 : 1);

        }

        private async Task<KeyValuePair<int, string>> TranslateTextAsync(int order, string text, string targetLanguage, 
            string sourceLanguage, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("Translating chunk {Order}: {Preview}...", order, 
                    text.Substring(0, Math.Min(100, text.Length)));
                
                string templateText = GetTranslateTemplate(targetLanguage, sourceLanguage, text);
                List<ContentFile> contents = [];

                ContentFile message = new()
                {
                    Type = "text",
                    Text = templateText
                };
                contents.Add(message);
                ClaudeRequestWithFile claudeRequest = new(contents);
                
                _logger.LogDebug("Sending chunk {Order} to Claude for translation", order);
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);

                string claudResponsePlainText = claudeResponse.Content.Single().Text;
                _logger.LogDebug("Received response for chunk {Order}, length: {Length} chars", 
                    order, claudResponsePlainText.Length);

                int start = claudResponsePlainText.IndexOf("<translation>", StringComparison.Ordinal);
                int end = claudResponsePlainText.IndexOf("</translation>", StringComparison.Ordinal);
                
                if (start >= 0 && end > start)
                {
                    start += 13; // Length of "<translation>"
                    string extractedText = claudResponsePlainText[start..end];
                    _logger.LogDebug("Successfully extracted translation for chunk {Order}, length: {Length} chars", 
                        order, extractedText.Length);
                    return new KeyValuePair<int, string>(order, extractedText);
                }
                
                if (claudResponsePlainText.Contains("I apologize") || 
                    claudResponsePlainText.Contains("I'm sorry") ||
                    claudResponsePlainText.Contains("don't see any image"))
                {
                    _logger.LogWarning("Claude returned an error message for chunk {Order}, attempting retry", order);
                    
                    string directPrompt = $"""

                                                                   TASK: Translate the following text from {sourceLanguage} to {targetLanguage}.
                                                                   
                                                                   TEXT TO TRANSLATE:
                                                                   {text}
                                                                   
                                                                   INSTRUCTIONS:
                                                                   - Provide ONLY the translated text
                                                                   - Do not include any explanations or notes
                                                                   - Do not mention that this is a translation
                                                                   - Do not apologize or explain your capabilities
                                                                   - This is a TEXT translation task, not an image task
                                                               
                                           """;
                    
                    ContentFile retryMessage = new()
                    {
                        Type = "text",
                        Text = directPrompt
                    };
                    
                    ClaudeRequestWithFile retryRequest = new([retryMessage]);
                    _logger.LogInformation("Retrying translation with simplified prompt for chunk {Order}", order);
                    
                    ClaudeResponse retryResponse = await _claudeService.SendRequestWithFile(retryRequest, cancellationToken);
                    string retryResult = retryResponse.Content.Single().Text.Trim();
                    
                    _logger.LogDebug("Retry translation for chunk {Order} returned {Length} chars", 
                        order, retryResult.Length);
                        
                    if (retryResult.Contains("I apologize") || retryResult.Contains("I'm sorry"))
                    {
                        _logger.LogWarning("Retry still returned an error for chunk {Order}, trying regular API", order);
                        
                        try
                        {
                            _logger.LogInformation("Sending fallback translation request to Claude API for chunk {Order}", order);
                            
                            ClaudeRequest finalRequest = new($"Translate this text to {targetLanguage}: {text}");
                            ClaudeResponse finalResponse = await _claudeService.SendRequest(finalRequest, cancellationToken);
                            string finalResult = finalResponse.Content.Single().Text.Trim();
                            
                            _logger.LogInformation("Successfully received fallback translation response for chunk {Order}, length: {Length} chars", 
                                order, finalResult.Length);
                            
                            return new KeyValuePair<int, string>(order, finalResult);
                        }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogError(fallbackEx, "Error occurred in fallback Claude API call for chunk {Order}", order);
                            return new KeyValuePair<int, string>(order, $"Error in fallback translation: {fallbackEx.Message}");
                        }
                    }
                    
                    return new KeyValuePair<int, string>(order, retryResult);
                }
                
                _logger.LogWarning("Could not find translation tags in Claude response for chunk {Order}, using full response", order);
                return new KeyValuePair<int, string>(order, claudResponsePlainText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error translating chunk {Order}", order);
                return new KeyValuePair<int, string>(order, $"Error translating text: {ex.Message}");
            }
        }

        private static List<string> GetChunksOfLargeText(string text, int chunkSize = 2000)
        {
            List<string> chunks = [];
            string[] sentences = Regex.Split(text, @"(?<=[.!?])\s+");

            StringBuilder currentChunk = new();

            foreach (string sentence in sentences)
            {
                if (currentChunk.Length + sentence.Length > chunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    _ = currentChunk.Clear();
                }

                _ = currentChunk.Append(sentence + " ");
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

        public async Task<VideoScriptAIResponse> VideoScript(IFormFile file, UserVideoScriptAIRequest request,
            string userId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting VideoScript request for user {UserId}", userId);
            
            decimal requestPrice = GetRequestPrice(RequestType.Copyright);

            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for VideoScript request. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                    userId, user.UserBalance.Balance, requestPrice);
                throw new Exception("VideoScriptAI რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

            string templateText = GetVideoScriptTemplate(language.Name, request.ProductName);

            ContentFile message = new()
            {
                Type = "text",
                Text = templateText
            };

            string extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
                _logger.LogWarning("Unsupported file extension for VideoScript: {Extension}. File: {FileName}", extention, file.FileName);
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            ContentFile fileMessage = new()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extention}",
                    Data = EncodeFileToBase64(file)
                }
            };

            ClaudeRequestWithFile claudeRequest = new([fileMessage, message]);
            
            try
            {
                _logger.LogInformation("Sending VideoScript request to Claude API. User: {UserId}, Language: {Language}, File: {FileName}", 
                    userId, language.Name, file.FileName);
                    
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for VideoScript. User: {UserId}", userId);
                
                string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

                int lastPeriod = claudResponseText.LastIndexOf('.');

                if (lastPeriod != -1)
                {
                    claudResponseText = new string([.. claudResponseText.Take(lastPeriod + 1)]);
                }

                VideoScriptAIResponse response = new()
                {
                    Text = claudResponseText
                };

                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = RequestType.VideoScript
                }, cancellationToken);

                await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);

                _logger.LogInformation("VideoScript request completed successfully. User: {UserId}, Response length: {ResponseLength}", 
                    userId, response.Text?.Length ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for VideoScript. User: {UserId}, Request: {Request}", 
                    userId, JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process VideoScript request: {ex.Message}", ex);
            }
        }

        public async Task<EmailAIResponse> Email(UserEmailRequest request, string userId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Email request for user {UserId}", userId);
            
            decimal requestPrice = GetRequestPrice(RequestType.Email);
            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for Email request. User: {UserId}, Balance: {Balance}, Required: {Required}", 
                    userId, user.UserBalance.Balance, requestPrice);
                throw new Exception("EmailAI რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

            string templateText = GetEmailTemplate(request.Email, language.Name, request.Form);

            ContentFile message = new()
            {
                Type = "text",
                Text = templateText
            };
            
            ClaudeRequestWithFile claudeRequest = new([message]);
            
            try
            {
                _logger.LogInformation("Sending Email request to Claude API. User: {UserId}, Language: {Language}, EmailLength: {EmailLength}, Form: {Form}", 
                    userId, language.Name, request.Email?.Length ?? 0, request.Form);
                    
                ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
                
                _logger.LogInformation("Successfully received response from Claude API for Email. User: {UserId}", userId);
                
                string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

                if (!claudResponseText.Contains("</response>"))
                {
                    int lastPeriod = claudResponseText.LastIndexOf('.');

                    if (lastPeriod != -1)
                    {
                        claudResponseText = new string([.. claudResponseText.Take(lastPeriod + 1)]);
                    }
                }

                EmailAIResponse response = new()
                {
                    Text = claudResponseText
                };

                await _requestLogService.Create(new CreateUserRequestLogModel
                {
                    UserId = userId,
                    Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    }),
                    Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }),
                    RequestType = RequestType.Email
                }, cancellationToken);

                await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);

                _logger.LogInformation("Email request completed successfully. User: {UserId}, Response length: {ResponseLength}", 
                    userId, response.Text?.Length ?? 0);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Claude API for Email. User: {UserId}, Request: {Request}", 
                    userId, JsonSerializer.Serialize(request, new JsonSerializerOptions 
                    { 
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    }));
                throw new Exception($"Failed to process Email request: {ex.Message}", ex);
            }
        }

        private static string ConvertAttributes(List<Domain.Models.Attribute> attributes)
        {
            StringBuilder resultBuilder = new();
            foreach (Domain.Models.Attribute attribute in attributes)
            {
                _ = resultBuilder.Append($"{attribute.Key}: {attribute.Value}; \n");
            }

            return resultBuilder.ToString();
        }

        private static string GetImageToTextTemplate(string sourceLanguage)
        {
            return $@"Here is the image you need to analyze: <image>{{IMAGE_TO_PROCESS}}</image>
              You are an advanced AI assistant with OCR (Optical Character Recognition) capabilities specialized in extracting text from images. 
              Your task is to analyze an image containing text in {sourceLanguage} and convert it into a properly formatted Markdown (.md) document. This may include both printed and handwritten text.

              Follow these steps to complete the OCR and text processing task:
              1. Carefully examine the image using your OCR capabilities to detect all visible text elements.
              2. Apply advanced OCR processing to handle various challenges:
                 - Different font styles and sizes
                 - Low contrast or poor image quality
                 - Rotated or skewed text
                 - Text on complex backgrounds
                 - Handwritten characters with varying styles
              3. Identify and distinguish between printed and handwritten text if both are present.
              4. For handwritten text:
                 - Focus on the shape and style of the characters
                 - Consider context clues to help decipher unclear words
                 - Be prepared to make educated guesses for ambiguous characters
              5. For printed text:
                 - Recognize standard fonts and typefaces
                 - Pay attention to formatting, such as bold or italic text
              6. Transcribe the text you see in the image, maintaining the original structure.
              7. If you encounter any text that is unclear or ambiguous, indicate this by placing the uncertain text in square brackets with a question mark, like this: [unclear word?]
              8. Format your transcription as a proper Markdown document:
                 - Use # for main headings, ## for subheadings, etc.
                 - Use **bold** and *italic* to represent formatting seen in the original text
                 - Use proper Markdown list formatting (- or 1. 2. 3.) for any lists in the text
                 - Use > for quoted text blocks
                 - Use proper paragraph spacing
                 - Use `code blocks` for any code or technical content
                 - Use tables if tabular data is present
              9. Include any relevant punctuation marks that you can identify in the image.
              10. If the image contains non-text elements (e.g., logos, drawings), briefly mention them in [Image: description] format.
              11. After completing the transcription, provide a brief note about the overall clarity and legibility as a comment at the end using <!-- comment --> syntax.
              
              Present your complete output as a properly formatted Markdown document that could be saved directly as a .md file.
              Remember, your primary goal is to accurately transcribe the {sourceLanguage} text from the image using your OCR capabilities, preserving the original formatting with Markdown syntax. Strive for the highest possible accuracy while also indicating any areas of uncertainty.";
        }

        private static string GetDocumentToMarkdownTemplate(string fileName)
        {
            return @$"You are an AI assistant tasked with converting Word or PDF documents into Markdown format while preserving the original document's structure, formatting, and visual elements. Your goal is to create a Markdown file that, when compiled, will replicate the original document as closely as possible. This process is designed to facilitate a more fluid document translation flow.

                Here are the steps you should follow:

                1. Analyze the input file:
                <input_file> {fileName} </input_file>

                Determine the file type (Word or PDF) and assess its content, including text, tables, images, and other visual elements.

                2. Process the document content:
                - Extract all text from the document, maintaining its original structure (headings, paragraphs, lists, etc.).
                - Identify and preserve any special formatting (bold, italic, underline, etc.).
                - Locate all tables and images within the document.

                3. Handle tables and visual elements:
                - For tables, convert them into Markdown table format. If the tables are complex, consider using HTML table syntax within the Markdown file for better representation.
                - For images, extract them and save them as separate files. In the Markdown document, use the appropriate Markdown syntax to reference these images.

                4. Convert to Markdown format:
                - Use appropriate Markdown syntax for headings, lists, emphasis, and links.
                - Ensure that the document's hierarchy and structure are maintained through proper use of Markdown headings (#, ##, ###, etc.).
                - Convert any footnotes or endnotes to Markdown format.

                5. Preserve document structure and formatting:
                - Maintain the original document's layout as closely as possible, including page breaks, columns, and text alignment.
                - If certain formatting cannot be replicated exactly in Markdown, use HTML and CSS within the Markdown file to achieve a similar appearance.

                6. Generate the output:
                Create a Markdown (.md) file that contains the converted content. Ensure that all references to external files (such as images) are correctly linked.

                7. Review and format check:
                - Verify that all content from the original document has been transferred to the Markdown file.
                - Check that the Markdown syntax is correct and will render properly when compiled.
                - Ensure that the overall structure and appearance of the document are preserved as much as possible.

                Your final output should be a well-formatted Markdown file that closely replicates the original document. Include only the converted Markdown content in your response, enclosed in <markdown_output> tags. Do not include any explanations or comments outside of these tags.

                <markdown_output>
                [Insert the converted Markdown content here]
                </markdown_output>";
        }
        private string GetDocumentToMarkdownTemplate1(string fileName)
        {
            return $@"You are an AI assistant tasked with converting Word or PDF documents into HTML format while preserving the original document's structure, formatting, and visual elements. Your goal is to create an HTML file that, when rendered, will replicate the original document as closely as possible. The resulting HTML should be formatted to fit an A4 page size to ensure compatibility with standard printing formats.

                    Here are the steps you should follow:

                    1. Analyze the input file:
                    <input_file> {fileName} </input_file>

                    Determine the file type (Word or PDF) and assess its content, including text, tables, images, and other visual elements.

                    2. Process the document content:
                    - Extract all text from the document, maintaining its original structure (headings, paragraphs, lists, etc.).
                    - Identify and preserve any special formatting (bold, italic, underline, etc.).
                    - Locate all tables and images within the document.

                    3. Handle tables and visual elements:
                    - For tables, convert them into HTML table format.
                    - For images, extract them and save them as separate files. In the HTML document, use the appropriate <img> tags to reference these images.

                    4. Convert to HTML format:
                    - Use appropriate HTML tags for headings, lists, emphasis, and links.
                    - Ensure that the document's hierarchy and structure are maintained through proper use of HTML elements (<h1>, <h2>, <p>, <ul>, <ol>, etc.).
                    - Convert any footnotes or endnotes to HTML format using appropriate tags such as <sup> and <a>.

                    5. Ensure A4 Page Compatibility:
                    - Use inline CSS or a <style> block to format the page for A4 size:
                    - Set the page dimensions to A4 size (210mm x 297mm).
                    - Include margins and padding to match standard print layout.
                    - Use @media print in the CSS to optimize the document for printing.
                    - Maintain the original document's layout as closely as possible, including page breaks, columns, and text alignment.

                    6. Generate the output:
                    Create an HTML (.html) file that contains the converted content. Ensure that all references to external files (such as images) are correctly linked.

                    7. Review and format check:
                    - Verify that all content from the original document has been transferred to the HTML file.
                    - Check that the HTML syntax is correct and will render properly when opened in a browser or printed.
                    - Ensure that the overall structure and appearance of the document are preserved as much as possible, with adherence to A4 format.

                    Your final output should be a well-formatted HTML file that closely replicates the original document. Include only the converted HTML content in your response, enclosed in <html_output> tags. Do not include any explanations or comments outside of these tags.

                    <html_output>
                    [Insert the converted HTML content here]
                    </html_output>";
        }

        private static string GetChainTranslateTemplate(string initialPrompt, string lastResponse)
        {
            return $@"you should continue translation of previously given text.
                      initial prompt is here <initial_prompt>{initialPrompt}</initial_prompt>
                      your last response is <last_response>{lastResponse}</last_response>
                      now you should continue translation from where last response is finished.
                      you should use translation rules from initial prompt";
        }

        private static string GetEnhanceTranslateTemplate(string targetLanguage, string userInput,
            string TranslateOutput)
        {
            return @$"<original_text>{userInput}</original_text>
                        <translated_text>{TranslateOutput}</translated_text>

                        You are a professional translator specializing in {targetLanguage}. Your task is to enhance the provided machine translation while maintaining EXACT meaning and content - no summarization, no restructuring, no content addition or removal.

                        Rules:
                        1. Keep the exact same number of paragraphs, sentences, and information points
                        2. Preserve all numbers, dates, contact information, and proper nouns exactly as they appear
                        3. DO NOT add headings or restructure the content
                        4. DO NOT summarize or condense the content
                        5. Focus only on improving language fluency and accuracy in {targetLanguage}

                        First, provide 2-3 brief improvement suggestions focused solely on language aspects:
                        <suggestions>
                        [Your language improvement suggestions in {targetLanguage}]
                        </suggestions>

                        <hr>

                        Then provide the enhanced translation that maintains the exact structure and content:
                        <enhanced_text>
                        [Your enhanced translation]
                        </enhanced_text>

                        Remember: The enhanced translation must contain ALL information from the original text, in the same order and structure. No information should be added, removed, or reorganized.";
        }


        private static string GetTranslateTemplate(string targetLanguage, string sourceLanguge, string description)
        {
            return @$"<text_to_translate> {description} </text_to_translate>
                      You are a highly skilled translator tasked with translating text from one language to another. You aim to provide highly accurate and natural-sounding translations while maintaining the original meaning and context. 
                      The target language for translation: <target_language>{targetLanguage}</target_language> from source language <source_language>{sourceLanguge}</source_language>.
                      
                      Important guidelines:
                      1. Convert all mathematical formulas and equations to regular text using Unicode symbols (e.g., α, β, π, ², ³, ≤, ≥, ±, ÷, ×). Do not use LaTeX formatting.
                      2. Preserve technical identifiers, standards (like ISO, EN), codes, and reference numbers in their original form
                      3. Translate technical terms using standard {targetLanguage} equivalents when they exist
                      4. **HUMAN NAMES**: When encountering human names (first names, last names, full names), always use TRANSLITERATION rather than translation - convert the name to {targetLanguage} script/alphabet while preserving the original pronunciation.
                         Examples: ""John Smith"" → transliterate to {targetLanguage} script, ""María González"" → transliterate to {targetLanguage} script
                      5. ALWAYS translate the provided text, even if it appears to be a placeholder value (like 'string', 'text', 'example', etc.). Treat all input as legitimate content to be translated.
                      
                      Always Provide your translation inside <translation></translation> tags. End translation with closing tag when the full text is translated.";
        }

        private static string GetCopyrightTemplate(string language, string productName)
        {
            return
                @$"Your task is to generate an engaging and effective Facebook advertisement text based on a provided image and an optional product name. 
                    The advertisement text should include relevant emojis and attention catching details to attract potential customers. 
                    I attached image that you should use and here is the optional product name (if not provided, leave blank):{productName}.
                    Do not make any promotional offer if not stated in the photo. Output text in {language} Language. 
                    Write minimum of 50 words long.";
        }

        private static string GetEmailTemplate(string mail, string language, EmailSpeechForm form)
        {
            return
                $@"You are an AI assistant tasked with responding to emails. You will be given a received email and a preferred speech form (either formal, neutral or familiar). 
                    Your job is to craft an appropriate response. Here is the received email: <received_email> {mail} </received_email> The preferred speech form for the response is: {form}.
                    Follow these steps to complete the task: Carefully read and analyze the received email. Pay attention to the content, tone, and any specific questions or requests. 
                    Craft an appropriate response, keeping in mind the following guidelines: Use the specified speech form (formal, neutral or familiar) consistently throughout the response. 
                    Address all points mentioned in the original email. Be polite and professional, regardless of the speech form. Keep the response concise but comprehensive. Translate your response into {language}. 
                    Ensure that the translation maintains the same tone and speech form as the English version. Output your final response in {language}, enclosed in <response> your response </response> tags. 
                    Remember to adjust your language and tone based on the specified speech form.";
        }

        private static string GetVideoScriptTemplate(string language, string productName)
        {
            return
                @$"You are an AI assistant that specializes in creating engaging advertising video scripts and descriptions for social media platforms like TikTok, Instagram Reels, and YouTube Shorts. 
                    Your task is to generate a video script and description in {language} based on a provided product photo and optional product name. 
                    I have attached product photo And here is the product name (if provided):{productName}. First, carefully analyze the product photo. 
                    Take note of the product's appearance, key features, and any text or branding visible. If a product name was provided, consider how it relates to the product's appearance and potential benefits. 
                    Next, brainstorm 2-3 engaging and creative video script ideas that highlight the product's features and benefits in an entertaining way. Consider the short video format and what would grab viewers' attention. 
                    Select the best video script idea and write out the full script in {language}. The script should be concise (under 240 seconds) but engaging, with a clear hook, product showcase, and call-to-action. 
                    Use language that resonates with the target audience. Format your response like this: [Full video script in Georgian] Remember, the goal is to create a short, attention-grabbing video that effectively showcases the product and encourages viewers to engage with the brand. 
                    Let your creativity shine while keeping the script and description concise and relevant.";
        }

        private static string GetDefaultTemplate(string productCategoryName, string language)
        {
            return
                @$"For {productCategoryName} generate creative annotation/description containing the product consistency, how to use, brand information, recommendations and other information. 
                    Output should be in paragraphs and in {language}. Output pure annotation formatted in HTML Language (Small Bold headers, Bullet points, paragraphs, various tags and etc), use br tags instead of \\n. 
                    Do not start with 'Here is the annotation of the products..', give only description text.";
        }

        private static string EncodeFileToBase64(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is null or empty", nameof(file));
            }
            
            try
            {
                using MemoryStream memoryStream = new();
                file.CopyTo(memoryStream);
                byte[] bytes = memoryStream.ToArray();
                
                if (bytes.Length == 0)
                {
                    throw new Exception("File content is empty after reading");
                }
                
                return Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error encoding file to Base64: {ex.Message}", ex);
            }
        }

        private static decimal GetRequestPrice(RequestType requestType)
        {
            return requestType switch
            {
                RequestType.Content => 1,
                RequestType.Copyright => 1,
                RequestType.Translate => 0.1m,
                RequestType.EnhanceTranslate => 0.25m,
                RequestType.VideoScript => 1,
                RequestType.Email => 1,
                RequestType.Lawyer => throw new NotImplementedException(),
                _ => 0
            };
        }

        private static async Task<(string, int)> GetPdfContentInStringAsync(IFormFile file)
        {

            var builder = new StringBuilder();
            var bytes = await PdfToByteArrayAsync(file);
            var pageCount = 0;
            using (var stream = new MemoryStream(bytes))
            {
                using (var pdfReader = new PdfReader(stream))
                {
                    var pdf = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                    pageCount = pdf.GetNumberOfPages();
                    for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                    {
                        var page = pdf.GetPage(i);
                        var text = PdfTextExtractor.GetTextFromPage(page);
                        builder.Append(text);
                    }
                }
                return (builder.ToString(), pageCount);
            }

        }

        private static async Task<byte[]> PdfToByteArrayAsync(IFormFile file)
        {
            using MemoryStream memoryStream = new();
            await file.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        private static string GetEmailSpeechForm(EmailSpeechForm form)
        {
            return form switch
            {
                EmailSpeechForm.Formal => "Formal",
                EmailSpeechForm.Familiar => "Familiar",
                _ => "Neutral"
            };
        }

        /// <summary>
        /// Handles Claude API errors and returns appropriate user-friendly error messages for background jobs
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="operationType">The type of operation (e.g., "translation", "content generation")</param>
        /// <param name="context">Additional context about the operation</param>
        /// <returns>A user-friendly error message</returns>
        private string HandleClaudeApiErrorForBackgroundJob(Exception ex, string operationType, string context = "")
        {
            _logger.LogError(ex, "Claude API error in background job for {OperationType}. Context: {Context}", operationType, context);

            return ex switch
            {
                // HTTP-related errors (likely Anthropic API issues)
                HttpRequestException httpEx when httpEx.Message.Contains("timeout") =>
                    $"The AI service is temporarily unavailable. Your {operationType} request could not be completed due to timeout. Please try again later.",

                HttpRequestException httpEx when httpEx.Message.Contains("503") || httpEx.Message.Contains("502") || httpEx.Message.Contains("500") =>
                    $"The Anthropic AI service is temporarily unavailable. Your {operationType} request could not be completed. Please try again in a few minutes.",

                HttpRequestException httpEx when httpEx.Message.Contains("429") =>
                    $"The AI service is experiencing high load. Your {operationType} request could not be completed. Please wait a few minutes and try again.",

                HttpRequestException httpEx when httpEx.Message.Contains("401") || httpEx.Message.Contains("403") =>
                    $"AI service authorization problem. Your {operationType} request could not be completed due to authentication error. Please contact support.",

                HttpRequestException =>
                    $"AI service connection problem. Your {operationType} request could not be completed. Please check your internet connection and try again.",

                // Task/Operation cancelled
                OperationCanceledException =>
                    $"Your {operationType} request was cancelled or timed out. Please try again.",

                // JSON/Serialization errors
                var jsonEx when jsonEx.Message.Contains("JSON") || jsonEx.Message.Contains("Deserialize") =>
                    $"Received invalid response from AI service. Your {operationType} request could not be completed. Please try again.",

                // Rate limiting or quota errors
                var quotaEx when quotaEx.Message.Contains("quota") || quotaEx.Message.Contains("rate") || quotaEx.Message.Contains("limit") =>
                    $"AI service quota exceeded. Your {operationType} request could not be completed. Please try again in a few hours.",

                // File-related errors
                var fileEx when fileEx.Message.Contains("file") && fileEx.Message.Contains("size") =>
                    $"File is too large for {operationType}. Please use a smaller file.",

                var fileEx when fileEx.Message.Contains("file") && fileEx.Message.Contains("format") =>
                    $"File format is not supported for {operationType}. Please check the file type.",

                // Content-related errors
                var contentEx when contentEx.Message.Contains("content") && contentEx.Message.Contains("empty") =>
                    $"File is empty or contains no processable text for {operationType}.",

                var contentEx when contentEx.Message.Contains("content") && contentEx.Message.Contains("large") =>
                    $"Content is too large for {operationType}. Please split into smaller parts.",

                var authEx when authEx.Message.Contains("API") && authEx.Message.Contains("key") =>
                    $"AI service configuration error. Your {operationType} request could not be completed. Please contact support.",

                OutOfMemoryException =>
                    $"File or content is too large for {operationType}. Please use a smaller file.",

                // Argument/validation errors
                ArgumentException argEx =>
                    $"Invalid parameters for {operationType}: {GetSafeErrorMessage(argEx.Message)}",

                // Generic fallback
                _ => $"Your {operationType} request could not be completed due to an unexpected error. Please try again or contact support."
            };
        }

        private static string GetSafeErrorMessage(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                return "Unknown error";

            var safeMessage = errorMessage
                .Replace("API key", "***")
                .Replace("token", "***")
                .Replace("password", "***")
                .Replace("secret", "***");

            if (safeMessage.Length > 200)
                safeMessage = safeMessage.Substring(0, 200) + "...";

            return safeMessage;
        }

        private static bool IsRetryableError(Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx when httpEx.Message.Contains("timeout") => true,
                HttpRequestException httpEx when httpEx.Message.Contains("503") || httpEx.Message.Contains("502") => true,
                HttpRequestException httpEx when httpEx.Message.Contains("429") => true,
                OperationCanceledException => true,
                _ when ex.Message.Contains("rate") || ex.Message.Contains("limit") => true,
                _ => false
            };
        }
        
    }

    public enum EmailSpeechForm
    {
        Formal = 1,
        Familiar = 2,
        Neutral = 3
    }
}

