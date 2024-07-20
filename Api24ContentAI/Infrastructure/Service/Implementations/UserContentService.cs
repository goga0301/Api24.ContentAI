using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
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

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class UserContentService : IUserContentService
    {
        private readonly IClaudeService _claudeService;
        private readonly IUserRequestLogService _requestLogService;
        private readonly IProductCategoryService _productCategoryService;
        private readonly ILanguageService _languageService;
        private readonly IUserRepository _userRepository;

        private static readonly string[] SupportedFileExtensions = new string[]
        {
            "jpeg",
            "png",
            "gif",
            "webp"
        };

        public UserContentService(IClaudeService claudeService,
                              IUserRequestLogService requestLogService,
                              IProductCategoryService productCategoryService,
                              ILanguageService languageService,
                              IUserRepository userRepository)
        {
            _claudeService = claudeService;
            _requestLogService = requestLogService;
            _productCategoryService = productCategoryService;
            _languageService = languageService;
            _userRepository = userRepository;
        }
        public async Task<CopyrightAIResponse> CopyrightAI(IFormFile file, UserCopyrightAIRequest request, string userId, CancellationToken cancellationToken)
        {
            var requestPrice = GetRequestPrice(RequestType.Copyright);
            var user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("CopyrightAI რექვესთების ბალანსი ამოიწურა");
            }

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);


            var templateText = GetCopyrightTemplate(language.Name, request.ProductName);

            var message = new ContentFile()
            {
                Type = "text",
                Text = templateText
            };

            var extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            var fileMessage = new ContentFile()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extention}",
                    Data = EncodeFileToBase64(file)
                }
            };

            var claudeRequest = new ClaudeRequestWithFile(new List<ContentFile>() { fileMessage, message });
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            var lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Copyright
            }, cancellationToken);

            await _userRepository.UpdateUserBalance(userId, user.UserBalance.Balance - requestPrice, cancellationToken);

            return new CopyrightAIResponse
            {
                Text = claudResponseText
            };
        }

        public async Task<ContentAIResponse> SendRequest(UserContentAIRequest request, string userId, CancellationToken cancellationToken)
        {
            var requestPrice = GetRequestPrice(RequestType.Copyright);

            var user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("ContentAI რექვესთების ბალანსი ამოიწურა");
            }

            var productCategory = await _productCategoryService.GetById(request.ProductCategoryId, cancellationToken);

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);

            var templateText = GetDefaultTemplate(productCategory.NameEng, language.Name);

            var claudRequestContent = $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

            var claudeRequest = new ClaudeRequest(claudRequestContent);

            var claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
            var lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Content

            }, cancellationToken);

            await _userRepository.UpdateUserBalance(userId, user.UserBalance.Balance - requestPrice, cancellationToken);

            return new ContentAIResponse
            {
                Text = claudResponseText
            };
        }

        public async Task<TranslateResponse> Translate(UserTranslateRequest request, string userId, CancellationToken cancellationToken)
        {
            var requestPrice = GetRequestPrice(RequestType.Copyright);

            var user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("Translate რექვესთების ბალანსი ამოიწურა");
            }

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);
            bool isText = true;
            var contents = new List<ContentFile>();
            if (!request.IsPdf)
            {
                foreach (var file in request.Files)
                {
                    var extention = file.FileName.Split('.').Last();
                    if (!SupportedFileExtensions.Contains(extention))
                    {
                        throw new Exception("ფაილი უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
                    }

                    var fileMessage = new ContentFile()
                    {
                        Type = "image",
                        Source = new Source()
                        {
                            Type = "base64",
                            MediaType = $"image/{extention}",
                            Data = EncodeFileToBase64(file)
                        }
                    };
                    contents.Add(fileMessage);
                }
                isText = false;
            }
            else
            {
                request.Description = await GetPdfContentInStringAsync(request.Files.FirstOrDefault());
            }
            var templateText = GetTranslateTemplateTest(language.Name, request.Description, isText);

            var message = new ContentFile()
            {
                Type = "text",
                Text = templateText
            };
            contents.Add(message);
            var claudeRequest = new ClaudeRequestWithFile(contents);
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
            var end = claudResponseText.LastIndexOf("</translation>");
            //if (end == -1)
            //{
            //    var messageContinue = new ContentFile()
            //    {
            //        Type = "text",
            //        Text = "continue"   
            //    };
            //    var continueReq = new List<ContentFile>() { messageContinue };
            //    var claudeRequestContinue = new ClaudeRequestWithFile(continueReq);
            //    var claudeResponseContinue = await _claudeService.SendRequestWithFile(claudeRequestContinue, cancellationToken);
            //    var claudResponseTextContinue = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
            //    claudResponseText += claudResponseTextContinue;
            //}
            end = claudResponseText.LastIndexOf("</translation>");
            if (end == -1)
            {
                throw new Exception("too long");
            }


            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Translate
            }, cancellationToken);

            await _userRepository.UpdateUserBalance(userId, user.UserBalance.Balance - requestPrice, cancellationToken);

            return new TranslateResponse
            {
                Text = claudResponseText
            };
        }
        //public async Task<TranslateResponse> Translate(UserTranslateRequest request, string userId, CancellationToken cancellationToken)
        //{
        //    var m = await GetPdfContentInStringAsync(request.Files.FirstOrDefault());
        //    var t = m.Length;
        //    var requestPrice = GetRequestPrice(RequestType.Copyright);

        //    var user = await _userRepository.GetById(userId, cancellationToken);

        //    if (user != null && user.UserBalance.Balance < requestPrice)
        //    {
        //        throw new Exception("Translate რექვესთების ბალანსი ამოიწურა");
        //    }

        //    var language = await _languageService.GetById(request.LanguageId, cancellationToken);

        //    var contents = new List<ContentFile>();
        //    var messages = new List<MessageWithFile>();
        //    var resultText = new StringBuilder();
        //    if (!request.IsPdf)
        //    {
        //        foreach (var file in request.Files)
        //        {
        //            var extention = file.FileName.Split('.').Last();
        //            if (!SupportedFileExtensions.Contains(extention))
        //            {
        //                throw new Exception("ფაილი უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
        //            }

        //            var fileMessage = new ContentFile()
        //            {
        //                Type = "image",
        //                Source = new Source()
        //                {
        //                    Type = "base64",
        //                    MediaType = $"image/{extention}",
        //                    Data = EncodeFileToBase64(file)
        //                }
        //            };
        //            contents.Add(fileMessage);
        //        }
        //    }
        //    else
        //    {
        //        request.Description = await GetPdfContentInStringAsync(request.Files.FirstOrDefault());
        //        int k = 0;
        //        while (true)
        //        {
        //            var chars = request.Description.Skip(k * 2000).Take(2000);
        //            var text = new string(chars.ToArray());
        //            var templateText = GetTranslateTemplate(language.Name, text);

        //            var message = new ContentFile()
        //            {
        //                Type = "text",
        //                Text = templateText
        //            };
        //            contents.Add(message);
        //            messages.Add(new MessageWithFile("user", new List<ContentFile> { message })); 

        //            var claudeRequest = new ClaudeRequestWithFile(new List<ContentFile>() { message});
        //            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
        //            var claudResponseText1 = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
        //            resultText.Append(claudResponseText1);
        //            if ( text.Length < 2000)
        //            {
        //                break;
        //            }
        //            k++;
        //        }

        //    }
        //    var ko = resultText.ToString();
        //    var claudResponseText = "";
        //    var lastPeriod = claudResponseText.LastIndexOf('.');

        //    if (lastPeriod != -1)
        //    {
        //        claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
        //    }

        //    await _requestLogService.Create(new CreateUserRequestLogModel
        //    {
        //        UserId = userId,
        //        Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
        //        {
        //            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        //            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        //        }),
        //        RequestType = RequestType.Translate
        //    }, cancellationToken);

        //    await _userRepository.UpdateUserBalance(userId, user.UserBalance.Balance - requestPrice, cancellationToken);

        //    return new TranslateResponse
        //    {
        //        Text = claudResponseText
        //    };
        //}

        public async Task<VideoScriptAIResponse> VideoScript(IFormFile file, UserVideoScriptAIRequest request, string userId, CancellationToken cancellationToken)
        {
            var requestPrice = GetRequestPrice(RequestType.Copyright);

            var user = await _userRepository.GetById(userId, cancellationToken);

            if (user != null && user.UserBalance.Balance < requestPrice)
            {
                throw new Exception("VideoScriptAI რექვესთების ბალანსი ამოიწურა");
            }

            var language = await _languageService.GetById(request.LanguageId, cancellationToken);


            var templateText = GetVideoScriptTemplate(language.Name, request.ProductName);

            var message = new ContentFile()
            {
                Type = "text",
                Text = templateText
            };

            var extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            var fileMessage = new ContentFile()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extention}",
                    Data = EncodeFileToBase64(file)
                }
            };

            var claudeRequest = new ClaudeRequestWithFile(new List<ContentFile>() { fileMessage, message });
            var claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            var claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            var lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            await _requestLogService.Create(new CreateUserRequestLogModel
            {
                UserId = userId,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.VideoScript
            }, cancellationToken);

            await _userRepository.UpdateUserBalance(userId, user.UserBalance.Balance - requestPrice, cancellationToken);

            return new VideoScriptAIResponse
            {
                Text = claudResponseText
            };
        }

        private string ConvertAttributes(List<Domain.Models.Attribute> attributes)
        {
            var resultBuilder = new StringBuilder();
            foreach (var attribute in attributes)
            {
                resultBuilder.Append($"{attribute.Key}: {attribute.Value}; \n");
            }
            return resultBuilder.ToString();
        }

        private string GetTranslateTemplate(string language, string description)
        {
            return $"You are a multilingual AI translation assistant. Your task is to translate product descriptions from one language to another and output the result in HTML format. The source language for the product description is: <source_language> {{ENGLISH}} </source_language>. The target language for the product description is: <target_language> {language} </target_language>. Here is the product description text to translate: <product_description> {description} </product_description>. Please translate the product description text from {{ENGLISH}} to {language}. Output the translated text in HTML format, enclosed within <translated_description> tags. Do not include any other explanations, notes, or caveats in your output. Only provide the translated text in the specified HTML tags.";
        }
        private string GetTranslateTemplateTest(string language, string description, bool isText)
        {
            var input = isText ? "text" : "image";
            return @$"You are a highly skilled translator tasked with translating text from one language to another. You aim to provide accurate and natural-sounding translations while maintaining the original meaning and context.

                     First, you will receive information about the input type:
                     
                     <input_type>
                     {input}
                     </input_type>
                     
                     Based on the input type, follow these instructions:
                     
                     1. If the input type is ""text"":
                        - Proceed directly to the translation task using the provided text.
                     
                     2. If the input type is ""image"":
                        - Process the image and take the text for translation.
                     
                     Next, you will receive the text to be translated:
                     
                     <text_to_translate>
                     {description}
                     </text_to_translate>
                     
                     The target language for translation:
                     
                     <target_language>
                     {language}
                     </target_language>
                     
                     When translating, follow these guidelines:
                     
                     1. For long texts:
                        - Divide the text into smaller, manageable paragraphs or sections.
                        - Translate each section individually, ensuring coherence between sections.
                        - If the text is extremely long, inform the user that you'll translate it in parts and provide updates on your progress.
                     
                     2. Maintain the original formatting, including paragraphs, bullet points, and numbered lists.
                     3. Pay attention to context and idiomatic expressions, translating them appropriately for the target language.
                     4. For proper nouns, brand names, or specific technical terms, keep them in their original form unless there's a widely accepted translation in the target language.
                     5. Translate full texts, do not commit any parts
                     
                     Provide your translation inside <translation> tags. 
                     
                     Begin your translation now.";
        }

        private string GetCopyrightTemplate(string language, string productName)
        {
            return $"Your task is to generate an engaging and effective Facebook ad text based on a provided image and an optional product name. The advertisement text should include relevant emojis and a promotional offer to entice potential customers. I attached image that you should use and here is the optional product name (if not provided, leave blank):{productName} Do not make any promotional offer if not stated in the photo. Output text in {language} Language. Write minimum of 500 characters.";
        }

        private string GetVideoScriptTemplate(string language, string productName)
        {
            return $"You are an AI assistant that specializes in creating engaging advertising video scripts and descriptions for social media platforms like TikTok, Instagram Reels, and YouTube Shorts. Your task is to generate a video script and description in {language} based on a provided product photo and optional product name. I have attached product photo And here is the product name (if provided):{productName}. First, carefully analyze the product photo. Take note of the product's appearance, key features, and any text or branding visible. If a product name was provided, consider how it relates to the product's appearance and potential benefits. Next, brainstorm 2-3 engaging and creative video script ideas that highlight the product's features and benefits in an entertaining way. Consider the short video format and what would grab viewers' attention. Select the best video script idea and write out the full script in {language}. The script should be concise (under 60 seconds) but engaging, with a clear hook, product showcase, and call-to-action. Use language that resonates with the target audience. Format your response like this: [Full video script in Georgian] Remember, the goal is to create a short, attention-grabbing video that effectively showcases the product and encourages viewers to engage with the brand. Let your creativity shine while keeping the script and description concise and relevant.";
        }

        private string GetDefaultTemplate(string productCategoryName, string language)
        {
            return $"For {productCategoryName} generate creative annotation/description containing the product consistency, how to use, brand information, recommendations and other information. Output should be in paragraphs and in {language}. Output pure annotation formatted in HTML Language (Small Bold headers, Bullet points, paragraphs, various tags and etc), use br tags instead of \\n. Do not start with 'Here is the annotation of the products..', give only description text.";
        }

        private static string EncodeFileToBase64(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return string.Empty;

            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                byte[] fileBytes = ms.ToArray();
                return Convert.ToBase64String(fileBytes);
            }
        }

        private static decimal GetRequestPrice(RequestType requestType)
        {
            return requestType switch
            {
                RequestType.Content => 10,
                RequestType.Copyright => 10,
                RequestType.Translate => 10,
                RequestType.VideoScript => 10,
                _ => 0
            };
        }

        private async Task<string> GetPdfContentInStringAsync(IFormFile file)
        {

            var builder = new StringBuilder();
            var bytes = await PdfToByteArrayAsync(file);
            using (var stream = new MemoryStream(bytes))
            {
                using (var pdfReader = new PdfReader(stream))
                {
                    var pdf = new iText.Kernel.Pdf.PdfDocument(pdfReader);
                    for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                    {
                        var page = pdf.GetPage(i);
                        var text = PdfTextExtractor.GetTextFromPage(page);
                        builder.Append(text);
                    }
                }
                return builder.ToString();
            }

        }


        private async Task<byte[]> PdfToByteArrayAsync(IFormFile file)
        {
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                return memoryStream.ToArray();
            }
        }
    }
}
