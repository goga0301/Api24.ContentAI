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

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class UserContentService(
        IClaudeService claudeService,
        IUserRequestLogService requestLogService,
        IProductCategoryService productCategoryService,
        ILanguageService languageService,
        IUserRepository userRepository,
        ILogger<UserContentService> logger) : IUserContentService
    {
        private readonly IClaudeService _claudeService = claudeService;
        private readonly IUserRequestLogService _requestLogService = requestLogService;
        private readonly IProductCategoryService _productCategoryService = productCategoryService;
        private readonly ILanguageService _languageService = languageService;
        private readonly IUserRepository _userRepository = userRepository;
        private readonly ILogger<UserContentService> _logger = logger;

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


                ContentFile message = new()
                {
                    Type = "text",
                    Text = request.Message
                };


                ClaudeRequestWithFile claudeRequest = new([message]);
                ClaudeResponse claudeResponse =
                    await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
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

                return response;
                
        }

        public async Task<CopyrightAIResponse> CopyrightAI(IFormFile file, UserCopyrightAIRequest request,
            string userId, CancellationToken cancellationToken)
        {
            decimal requestPrice = GetRequestPrice(RequestType.Copyright);
            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("CopyrightAI რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);


            string templateText = GetCopyrightTemplate(language.Name, request.ProductName);

            ContentFile message = new()
            {
                Type = "text",
                Text = templateText
            };

            string extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
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
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
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

            return response;
        }

        public async Task<ContentAIResponse> SendRequest(UserContentAIRequest request, string userId,
            CancellationToken cancellationToken)
        {
            decimal requestPrice = GetRequestPrice(RequestType.Copyright);

            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("ContentAI რექვესთების ბალანსი ამოიწურა");
            }

            ProductCategoryModel productCategory =
                await _productCategoryService.GetById(request.ProductCategoryId, cancellationToken);

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

            string templateText = GetDefaultTemplate(productCategory.NameEng, language.Name);

            string claudRequestContent =
                $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

            ClaudeRequest claudeRequest = new(claudRequestContent);

            ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
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

            return response;
        }

        public async Task<TranslateResponse> ChunkedTranslate(UserTranslateRequest request, string userId,
            CancellationToken cancellationToken)
        {
            int pdfPageCount = 0;
            string description = request.Description ?? string.Empty;
            
            if (request.IsPdf && request.Files != null && request.Files.Count > 0)
            {
                _logger.LogInformation("Processing PDF file for translation");
                (description, pdfPageCount) = await GetPdfContentInStringAsync(request.Files.FirstOrDefault()).ConfigureAwait(false);
                _logger.LogInformation("Extracted {CharCount} characters from {PageCount} PDF pages", 
                    description?.Length ?? 0, pdfPageCount);
            }
            
            else if (string.IsNullOrWhiteSpace(description) && request.Files != null && request.Files.Count > 0)
            {
                _logger.LogInformation("Processing {Count} image files for translation", request.Files.Count);
                StringBuilder textFromImage = new();
                List<Task<KeyValuePair<int, string>>> tasksForImages = [];
                int indexForImages = 0;

                foreach (IFormFile file in request.Files)
                {
                    string extension = file.FileName.Split('.').Last().ToLower();
                    if (!SupportedFileExtensions.Contains(extension))
                    {
                        _logger.LogWarning("Unsupported file format: {Extension}", extension);
                        throw new Exception($"ფაილი უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: {string.Join(", ", SupportedFileExtensions)}!");
                    }

                    _logger.LogDebug("Processing image file: {FileName}, size: {Length} bytes", file.FileName, file.Length);
                    
                    // Create a separate task for each image to process them in parallel
                    int currentIndex = indexForImages++;
                    Task<KeyValuePair<int, string>> task = Task.Run(async () => {
                        try {
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

                            LanguageModel sourceLanguage =
                                await _languageService.GetById(request.SourceLanguageId, cancellationToken);
                            string templateTextForImageToText = GetImageToTextTemplate(sourceLanguage.Name);
                            ContentFile messageImageToText = new()
                            {
                                Type = "text",
                                Text = templateTextForImageToText
                            };

                            List<ContentFile> continueReq = [fileMessage, messageImageToText];
                            ClaudeRequestWithFile claudeRequestImageToText = new(continueReq);
                            
                            _logger.LogInformation("Sending image {Index} to Claude for OCR", currentIndex);
                            ClaudeResponse claudeResponseImageToText = await _claudeService.SendRequestWithFile(claudeRequestImageToText, cancellationToken);
                            string claudResponseTextContinueImageToText = claudeResponseImageToText.Content.Single().Text;
                            
                            _logger.LogDebug("Claude OCR response for image {Index}: {Length} chars", 
                                currentIndex, claudResponseTextContinueImageToText.Length);
                            
                            int start = claudResponseTextContinueImageToText.IndexOf("<transcription>");
                            int end = claudResponseTextContinueImageToText.IndexOf("</transcription>");
                            
                            if (start >= 0 && end > start)
                            {
                                start += 15;
                                string extractedText = claudResponseTextContinueImageToText[start..end];
                                _logger.LogDebug("Successfully extracted text from image {Index}, length: {Length} chars", 
                                    currentIndex, extractedText.Length);
                                return new KeyValuePair<int, string>(currentIndex, extractedText);
                            }
                            
                            start = claudResponseTextContinueImageToText.IndexOf("```markdown");
                            if (start >= 0)
                            {
                                start += 10; // Length of "```markdown"
                                end = claudResponseTextContinueImageToText.IndexOf("```", start);
                                if (end > start)
                                {
                                    string extractedText = claudResponseTextContinueImageToText[start..end].Trim();
                                    _logger.LogDebug("Extracted text from markdown block in image {Index}, length: {Length} chars", 
                                        currentIndex, extractedText.Length);
                                    return new KeyValuePair<int, string>(currentIndex, extractedText);
                                }
                            }
                            
                            string cleanedResponse = claudResponseTextContinueImageToText;
                            if (cleanedResponse.Contains("I apologize") || cleanedResponse.Contains("I'm sorry"))
                            {
                                _logger.LogWarning("Claude returned an apology for image {Index}, attempting to extract useful content", currentIndex);
                                
                                string directPrompt = "Extract all text visible in this image. Return ONLY the extracted text with no explanations or commentary:";
                                
                                ContentFile retryMessage = new()
                                {
                                    Type = "text",
                                    Text = directPrompt
                                };
                                
                                List<ContentFile> retryReq = [fileMessage, retryMessage];
                                ClaudeRequestWithFile retryRequest = new(retryReq);
                                
                                _logger.LogInformation("Retrying OCR for image {Index} with simplified prompt", currentIndex);
                                ClaudeResponse retryResponse = await _claudeService.SendRequestWithFile(retryRequest, cancellationToken);
                                string retryResult = retryResponse.Content.Single().Text.Trim();
                                
                                if (!retryResult.Contains("I apologize") && !retryResult.Contains("I'm sorry"))
                                {
                                    _logger.LogDebug("Retry OCR successful for image {Index}, got {Length} chars", 
                                        currentIndex, retryResult.Length);
                                    return new KeyValuePair<int, string>(currentIndex, retryResult);
                                }
                                
                                _logger.LogWarning("Retry OCR also failed for image {Index}", currentIndex);
                            }
                            
                            _logger.LogWarning("Could not find transcription tags in Claude response for image {Index}", currentIndex);
                            return new KeyValuePair<int, string>(currentIndex, cleanedResponse);
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "Error processing image {Index}", currentIndex);
                            return new KeyValuePair<int, string>(currentIndex, string.Empty);
                        }
                    }, cancellationToken);
                    
                    tasksForImages.Add(task);
                }

                if (tasksForImages.Count > 0)
                {
                    _logger.LogInformation("Waiting for {Count} image processing tasks to complete", tasksForImages.Count);
                    KeyValuePair<int, string>[] pairs = await Task.WhenAll(tasksForImages);
                    
                    foreach (KeyValuePair<int, string> result in pairs.OrderBy(r => r.Key))
                    {
                        if (!string.IsNullOrWhiteSpace(result.Value))
                        {
                            _logger.LogDebug("Adding text from image {Index}: {Preview}", 
                                result.Key, result.Value.Substring(0, Math.Min(50, result.Value.Length)) + "...");
                            textFromImage.AppendLine(result.Value);
                            textFromImage.AppendLine();
                        }
                    }
                    
                    description = textFromImage.ToString();
                    
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        _logger.LogWarning("No text extracted from any of the {Count} images", request.Files.Count);
                        throw new Exception("Could not extract any text from the provided images. Please check the image format and quality, or provide text directly.");
                    }
                    
                    _logger.LogInformation("Successfully extracted {Length} characters of text from {Count} images", 
                        description.Length, request.Files.Count);
                }
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                _logger.LogWarning("No text to translate - neither description nor extracted text from files");
                throw new Exception("No text to translate. Please provide text in the description field or upload files containing text.");
            }

            decimal requestPrice = CalculateTranslateRequestPriceNew(description);
            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                _logger.LogWarning("Insufficient balance for user {UserId}. Required: {Price}, Available: {Balance}", 
                    userId, requestPrice, user.UserBalance.Balance);
                throw new Exception("ბალანსი არ არის საკმარისი მოთხოვნის დასამუშავებლად!!!");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);
            LanguageModel sourceLanguage = await _languageService.GetById(request.SourceLanguageId, cancellationToken);

            _logger.LogInformation("Translating {Length} characters from {SourceLanguage} to {TargetLanguage}", 
                description.Length, sourceLanguage.Name, language.Name);

            List<string> chunks = GetChunksOfLargeText(description);
            StringBuilder translatedText = new();
            List<Task<KeyValuePair<int, string>>> tasks = [];

            for (int i = 0; i < chunks.Count; i++)
            {
                string chunk = chunks[i];
                _logger.LogDebug("Creating translation task for chunk {Index}, length: {Length} chars", i, chunk.Length);
                Task<KeyValuePair<int, string>> task = TranslateTextAsync(i, chunk, language.Name, sourceLanguage.Name, cancellationToken);
                tasks.Add(task);
            }

            _logger.LogInformation("Waiting for {Count} translation tasks to complete", tasks.Count);
            KeyValuePair<int, string>[] results = await Task.WhenAll(tasks);
            IEnumerable<KeyValuePair<int, string>> orderedResults = results.OrderBy(r => r.Key);

            foreach (KeyValuePair<int, string> kvp in orderedResults)
            {
                string translatedChunk = kvp.Value;
                _ = translatedText.AppendLine(translatedChunk);
            }

            string finalTranslation = translatedText.ToString().Replace("\n", "<br>").Replace("\r", "<br>");
            _logger.LogInformation("Translation completed, final length: {Length} characters", finalTranslation.Length);

            TranslateResponse response = new()
            {
                Text = finalTranslation
            };

            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = "translate text",
                RequestType = RequestType.Translate,
            }, cancellationToken);
            await _userRepository.UpdateUserBalance(userId, -requestPrice, cancellationToken);

            return response;
        }


        public async Task<TranslateResponse> EnhanceTranslate(UserTranslateEnhanceRequest request, string userId,
            CancellationToken cancellationToken)
        {
            decimal requestPrice = GetRequestPrice(RequestType.EnhanceTranslate);

            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("EnhanceTranslate რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel targetLanguage = await _languageService.GetById(request.TargetLanguageId, cancellationToken);

            string templateText =
                GetEnhanceTranslateTemplate(targetLanguage.Name, request.UserInput, request.TranslateOutput);
            StringBuilder wholeRequest = new(templateText);
            _ = wholeRequest.AppendLine("-----------------------------------");
            _ = wholeRequest.AppendLine();
            _ = wholeRequest.AppendLine();
            _ = wholeRequest.AppendLine();
            List<ContentFile> contents = [];

            ContentFile message = new()
            {
                Type = "text",
                Text = templateText
            };
            contents.Add(message);
            ClaudeRequestWithFile claudeRequest = new(contents);
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            string claudResponsePlainText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            TranslateResponse response = new()
            {
                Text = claudResponsePlainText
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
                RequestType = RequestType.EnhanceTranslate

            }, cancellationToken);

            await _userRepository.UpdateUserBalance(userId, requestPrice, cancellationToken);

            return response;
        }

        private static decimal CalculateTranslateRequestPrice(UserTranslateRequest request, int pdfPageCount)
        {
            decimal defaultPrice = GetRequestPrice(RequestType.Translate);

            return !string.IsNullOrWhiteSpace(request.Description) &&
                   (request.Files == null || request.Files.Count == 0)
                ? defaultPrice * ((request.Description.Length / 250) +
                                  (request.Description.Length % 250 == 0 ? 0 : 1))
                : request.Files != null && request.Files.Count == 1 && request.IsPdf
                    ? pdfPageCount * 0.9m
                    : request.Files.Count * 1.45m;
        }

        private static decimal CalculateTranslateRequestPriceNew(string description)
        {
            decimal defaultPrice = GetRequestPrice(RequestType.Translate);

            return defaultPrice * ((description.Length / 250) + (description.Length % 250) == 0 ? 0 : 1);

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

                int start = claudResponsePlainText.IndexOf("<translation>");
                int end = claudResponsePlainText.IndexOf("</translation>");
                
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
                    
                    string directPrompt = $@"
                        TASK: Translate the following text from {sourceLanguage} to {targetLanguage}.
                        
                        TEXT TO TRANSLATE:
                        {text}
                        
                        INSTRUCTIONS:
                        - Provide ONLY the translated text
                        - Do not include any explanations or notes
                        - Do not mention that this is a translation
                        - Do not apologize or explain your capabilities
                        - This is a TEXT translation task, not an image task
                    ";
                    
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
                        
                        ClaudeRequest finalRequest = new($"Translate this text to {targetLanguage}: {text}");
                        ClaudeResponse finalResponse = await _claudeService.SendRequest(finalRequest, cancellationToken);
                        string finalResult = finalResponse.Content.Single().Text.Trim();
                        
                        return new KeyValuePair<int, string>(order, finalResult);
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
            decimal requestPrice = GetRequestPrice(RequestType.Copyright);

            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
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
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
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

            return response;
        }

        public async Task<EmailAIResponse> Email(UserEmailRequest request, string userId,
            CancellationToken cancellationToken)
        {
            decimal requestPrice = GetRequestPrice(RequestType.Email);
            User user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
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
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
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

            return response;
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

        private static string GetDocumentToMarkdownTemplate()
        {
            return $@"Here is the document you need to convert: <input_file>{{DOCUMENT_TO_PROCESS}}</input_file>
              You are an AI assistant tasked with converting Word or PDF documents into Markdown format while preserving the original document's structure, formatting, and visual elements. Your goal is to create a Markdown file that, when compiled, will replicate the original document as closely as possible. This process is designed to facilitate a more fluid document translation flow.

              Follow these steps to complete the conversion task:
              1. Analyze the input file:
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
                      Always Provide your translation inside <translation></translation> tags. End translation with closing tag when the full text is translated. 
                      
                      When translating, follow these guidelines:
                      1. Maintain the original formatting, including paragraphs, bullet points, and numbered lists.
                      2. Pay attention to context and idiomatic expressions, translating them appropriately for the target language.
                      3. For proper nouns, brand names, or specific technical terms, keep them in their original form unless there's a widely accepted translation in the target language.
                      4. Translate full texts, do not commit any parts.
                      
                      Begin your translation now.";
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

            StringBuilder builder = new();
            byte[] bytes = await PdfToByteArrayAsync(file);
            int pageCount = 0;
            using MemoryStream stream = new(bytes);
            using (PdfReader pdfReader = new(stream))
            {
                PdfDocument pdf = new(pdfReader);
                pageCount = pdf.GetNumberOfPages();
                for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                {
                    PdfPage page = pdf.GetPage(i);
                    string text = PdfTextExtractor.GetTextFromPage(page);
                    _ = builder.Append(text);
                }
            }

            return (builder.ToString(), pageCount);

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
        
    }

    public enum EmailSpeechForm
    {
        Formal = 1,
        Familiar = 2,
        Neutral = 3
    }
}

