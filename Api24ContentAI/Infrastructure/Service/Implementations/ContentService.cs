using Api24ContentAI.Controllers;
using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ContentService : IContentService
    {
        private readonly IClaudeService _claudeService;
        private readonly ICustomTemplateService _customTemplateService;
        private readonly ITemplateService _templateService;
        private readonly IRequestLogService _requestLogService;
        private readonly IProductCategoryService _productCategoryService;
        private readonly IMarketplaceService _marketplaceService;
        private readonly ILanguageService _languageService;
        private readonly HttpClient _httpClient;
        // NOTE: caching service
        private readonly ICacheService _cacheService;


        private static readonly string[] SupportedFileExtensions = new string[]
        {
            "jpeg",
            "png",
            "gif",
            "webp"
        };

        public ContentService(IClaudeService claudeService,
                              ICacheService cacheService,
                              ICustomTemplateService customTemplateService,
                              ITemplateService templateService,
                              IRequestLogService requestLogService,
                              IProductCategoryService productCategoryService,
                              IMarketplaceService marketplaceService,
                              ILanguageService languageService,
                              HttpClient httpClient)
        {
            _claudeService = claudeService;
            _cacheService = cacheService;
            _customTemplateService = customTemplateService;
            _templateService = templateService;
            _requestLogService = requestLogService;
            _productCategoryService = productCategoryService;
            _marketplaceService = marketplaceService;
            _languageService = languageService;
            _httpClient = httpClient;
        }

        public async Task<ContentAIResponse> SendRequest(ContentAIRequest request, CancellationToken cancellationToken)
        {

            // NOTE: it would be good to implement caching layer to reduce api requests
            string cacheKey = GetCacheKey(request);

            return await _cacheService.GetOrCreateAsync<ContentAIResponse>(
                cacheKey,
                async () => {
                    MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

                    if (marketplace == null)
                    {
                    throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
                    }

                    if (marketplace.ContentLimit <= 0)
                    {
                    throw new Exception("ContentAI რექვესთების ბალანსი ამოიწურა");
                    }

                    ProductCategoryModel productCategory = await _productCategoryService.GetById(request.ProductCategoryId, cancellationToken);

                    LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

                    string templateText = GetDefaultTemplate(productCategory.NameEng, language.Name);

                    TemplateModel template = await _templateService.GetByProductCategoryId(request.ProductCategoryId, cancellationToken);

                    if (template != null)
                    {
                        templateText = template.Text;
                    }

                    CustomTemplateModel customTemplate = await _customTemplateService.GetByMarketplaceAndProductCategoryId(request.UniqueKey, request.ProductCategoryId, cancellationToken);

                    if (customTemplate != null)
                    {
                        templateText = customTemplate.Text;
                    }

                    string claudRequestContent = $"{request.ProductName} {templateText} {language.Name} \n Product attributes are: \n {ConvertAttributes(request.Attributes)}";

                    ClaudeRequest claudeRequest = new ClaudeRequest(claudRequestContent);
                    ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
                    string claudResponseText = ProcessClaudeResponse(claudeResponse);
                    ContentAIResponse response = new ContentAIResponse{ Text = claudResponseText };

                    await LogRequest(request, response, marketplace.Id, cancellationToken);
                    await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.Content);

                    return response;
            },
            TimeSpan.FromHours(24),
            cancellationToken
            );

            //await _marketplaceService.Update(new UpdateMarketplaceModel
            //{
            //    Id = request.UniqueKey,
            //    Name = marketplace.Name,
            //    TranslateLimit = marketplace.TranslateLimit,
            //    ContentLimit = marketplace.ContentLimit - 1,
            //}, cancellationToken);

        }

        public async Task<TranslateResponse> Translate(TranslateRequest request, CancellationToken cancellationToken)
        {
            MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace == null)
            {
                throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
            }

            if (marketplace.TranslateLimit <= 0)
            {
                throw new Exception("Translate რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);

            string templateText = GetTranslateTemplate(language.Name, request.Description);

            ClaudeRequest claudeRequest = new ClaudeRequest(templateText);
            ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
            string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            //var lastPeriod = claudResponseText.LastIndexOf('.');

            Guid biblusi = Guid.Parse("7254d5ec-1f47-470c-91af-3086349d425f");
            //if (lastPeriod != -1)
            //{
            //    claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            //}

            if (request.UniqueKey == biblusi && request.LanguageId == 5) // slovenian
            {
                claudResponseText += "<br> Opisi izdelkov so prevedeni s pomočjo umetne inteligence.";
            }

            TranslateResponse response = new TranslateResponse
            {
                Text = claudResponseText
            };

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Translate
            }, cancellationToken);

            //await _marketplaceService.Update(new UpdateMarketplaceModel
            //{
            //    Id = request.UniqueKey,
            //    Name = marketplace.Name,
            //    TranslateLimit = marketplace.TranslateLimit - 1,
            //    ContentLimit = marketplace.ContentLimit,
            //}, cancellationToken);

            await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.Translate);


            return response;
        }

        public async Task<TranslateResponse> EnhanceTranslate(EnhanceTranslateRequest request, CancellationToken cancellationToken)
        {
            MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace == null)
            {
                throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
            }

            if (marketplace.TranslateLimit <= 0)
            {
                throw new Exception("Translate რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel targetLanguage = await _languageService.GetById(request.TargetLanguageId, cancellationToken);

            string templateText = GetEnhanceTranslateTemplate(targetLanguage.Name, request.UserInput, request.TranslateOutput);

            ClaudeRequest claudeRequest = new ClaudeRequest(templateText);
            ClaudeResponse claudeResponse = await _claudeService.SendRequest(claudeRequest, cancellationToken);
            string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");


            Guid biblusi = Guid.Parse("7254d5ec-1f47-470c-91af-3086349d425f");

            if (request.UniqueKey == biblusi && request.TargetLanguageId == 5) // slovenian
            {
                claudResponseText += "<br> Opisi izdelkov so prevedeni s pomočjo umetne inteligence.";
            }

            TranslateResponse response = new TranslateResponse
            {
                Text = claudResponseText
            };

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.EnhanceTranslate
            }, cancellationToken);

            await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.EnhanceTranslate);


            return response;
        }

        public async Task<CopyrightAIResponse> CopyrightAI(IFormFile file, CopyrightAIRequest request, CancellationToken cancellationToken)
        {
            MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace == null)
            {
                throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
            }

            if (marketplace.TranslateLimit <= 0)
            {
                throw new Exception("CopyrightAI რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);


            string templateText = GetCopyrightTemplate(language.Name, request.ProductName);

            ContentFile message = new ContentFile()
            {
                Type = "text",
                Text = templateText
            };

            string extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            ContentFile fileMessage = new ContentFile()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extention}",
                    Data = EncodeFileToBase64(file)
                }
            };

            ClaudeRequestWithFile claudeRequest = new ClaudeRequestWithFile(new List<ContentFile>() { fileMessage, message });
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            int lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            CopyrightAIResponse response = new CopyrightAIResponse
            {
                Text = claudResponseText
            };

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Copyright
            }, cancellationToken);

            //await _marketplaceService.Update(new UpdateMarketplaceModel
            //{
            //    Id = request.UniqueKey,
            //    Name = marketplace.Name,
            //    TranslateLimit = marketplace.TranslateLimit,
            //    ContentLimit = marketplace.ContentLimit,
            //    CopyrightLimit = marketplace.CopyrightLimit - 1,
            //}, cancellationToken);

            await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.Copyright);

            return response;
        }

        public async Task<VideoScriptAIResponse> VideoScript(IFormFile file, VideoScriptAIRequest request, CancellationToken cancellationToken)
        {
            MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace == null)
            {
                throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
            }

            if (marketplace.TranslateLimit <= 0)
            {
                throw new Exception("VideoScriptAI რექვესთების ბალანსი ამოიწურა");
            }

            LanguageModel language = await _languageService.GetById(request.LanguageId, cancellationToken);


            string templateText = GetVideoScriptTemplate(language.Name, request.ProductName);

            ContentFile message = new ContentFile()
            {
                Type = "text",
                Text = templateText
            };

            string extention = file.FileName.Split('.').Last();
            if (!SupportedFileExtensions.Contains(extention))
            {
                throw new Exception("ფოტო უნდა იყოს შემდეგი ფორმატებიდან ერთერთში: jpeg, png, gif, webp!");
            }

            ContentFile fileMessage = new ContentFile()
            {
                Type = "image",
                Source = new Source()
                {
                    Type = "base64",
                    MediaType = $"image/{extention}",
                    Data = EncodeFileToBase64(file)
                }
            };

            ClaudeRequestWithFile claudeRequest = new ClaudeRequestWithFile(new List<ContentFile>() { fileMessage, message });
            ClaudeResponse claudeResponse = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            string claudResponseText = claudeResponse.Content.Single().Text.Replace("\n", "<br>");

            int lastPeriod = claudResponseText.LastIndexOf('.');

            if (lastPeriod != -1)
            {
                claudResponseText = new string(claudResponseText.Take(lastPeriod + 1).ToArray());
            }

            VideoScriptAIResponse response = new VideoScriptAIResponse
            {
                Text = claudResponseText
            };

            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(response, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.VideoScript
            }, cancellationToken);

            //await _marketplaceService.Update(new UpdateMarketplaceModel
            //{
            //    Id = request.UniqueKey,
            //    Name = marketplace.Name,
            //    TranslateLimit = marketplace.TranslateLimit,
            //    ContentLimit = marketplace.ContentLimit,
            //    CopyrightLimit = marketplace.CopyrightLimit,
            //    VideoScriptLimit = marketplace.VideoScriptLimit - 1,
            //    LawyerLimit = marketplace.LawyerLimit,
            //}, cancellationToken);

            await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.VideoScript);

            return response;
        }

        public async Task<LawyerResponse> Lawyer(LawyerRequest request, CancellationToken cancellationToken)
        {
            MarketplaceModel marketplace = await _marketplaceService.GetById(request.UniqueKey, cancellationToken);

            if (marketplace == null)
            {
                throw new Exception("შესაბამისი მარკეტფლეისი ვერ მოიძებნა!");
            }

            if (marketplace.LawyerLimit <= 0)
            {
                throw new Exception("Lawyer რექვესთების ბალანსი ამოიწურა");
            }

            PromptResponse response = await _httpClient.GetFromJsonAsync<PromptResponse>($"http://localhost:8000/rag/?prompt={request.Prompt}&k=5&model=claude-3-sonnet-20240229", cancellationToken);
            LawyerResponse result = new LawyerResponse
            {
                Text = response.Response
            };
            await _requestLogService.Create(new CreateRequestLogModel
            {
                MarketplaceId = request.UniqueKey,
                Request = JsonSerializer.Serialize(request, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                Response = JsonSerializer.Serialize(result, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                }),
                RequestType = RequestType.Lawyer
            }, cancellationToken);

            //await _marketplaceService.Update(new UpdateMarketplaceModel
            //{
            //    Id = request.UniqueKey,
            //    Name = marketplace.Name,
            //    TranslateLimit = marketplace.TranslateLimit,
            //    ContentLimit = marketplace.ContentLimit,
            //    CopyrightLimit = marketplace.CopyrightLimit,
            //    VideoScriptLimit = marketplace.VideoScriptLimit,
            //    LawyerLimit = marketplace.LawyerLimit - 1,
            //}, cancellationToken);

            await _marketplaceService.UpdateBalance(marketplace.Id, RequestType.Lawyer);

            return result;
        }

        private string ConvertAttributes(List<Domain.Models.Attribute> attributes)
        {
            StringBuilder resultBuilder = new StringBuilder();
            foreach (Domain.Models.Attribute attribute in attributes)
            {
                resultBuilder.Append($"{attribute.Key}: {attribute.Value}; \n");
            }
            return resultBuilder.ToString();
        }

        private string GetTranslateTemplate(string language, string description)
        {
            return $"You are a multilingual AI translation assistant. Your task is to translate product descriptions from one language to another and output the result in HTML format. The source language for the product description is: <source_language> {{ENGLISH}} </source_language>. The target language for the product description is: <target_language> {language} </target_language>. Here is the product description text to translate: <product_description> {description} </product_description>. Please translate the product description text from {{ENGLISH}} to {language}. Output the translated text in HTML format, enclosed within <translated_description> tags. Do not include any other explanations, notes, or caveats in your output. Only provide the translated text in the specified HTML tags. if any type of separator are detected (for example -- 1 --) keep the original formating";
        }

        private string GetEnhanceTranslateTemplate(string targetLanguage, string userInput, string TranslateOutput)
        {
            return @$"  <original_text>{userInput}</original_text>, 
                        <translated_text>{TranslateOutput}</translated_text>
                        
                        Imagine you are a highly skilled Multilingual translator. You are given an original text, and it's translated version in {targetLanguage} Language. 
                        
                        I want you to give me a text improvement suggestions enclosed in <suggestions> your response </suggestions> tags. 
                        Then apply suggestions to the translated_text and output it enclosed in <enhanced_text>text<enhanced_text> tags. 
                        Separate this tags using single <hr> tag.
                        Suggestions should not start with any introduction text. be maximum 50 Characters long, and be in {targetLanguage} Language.";
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

            using (MemoryStream ms = new MemoryStream())
            {
                file.CopyTo(ms);
                byte[] fileBytes = ms.ToArray();
                return Convert.ToBase64String(fileBytes);
            }
        }

        private string GetCacheKey(ContentAIRequest request)
        {
            return $"content_ai_{request.UniqueKey}_{request.ProductCategoryId}_{request.LanguageId}_{request.ProductName}_{string.Join("_", request.Attributes)}";
        }


        private string ProcessClaudeResponse(ClaudeResponse claudeResponse)
        {
            string text = claudeResponse.Content.Single().Text.Replace("\n", "<br>");
            int lastPeriod = text.LastIndexOf('.');
            if (lastPeriod != -1)
            {
                text = new string(text.Take(lastPeriod + 1).ToArray());
            }
            return text;
        }

        private async Task LogRequest(ContentAIRequest request, ContentAIResponse response,
                Guid marketplaceId, CancellationToken cancellationToken)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            };

            await _requestLogService.Create(new CreateRequestLogModel
                    {
                    MarketplaceId = marketplaceId,
                    Request = JsonSerializer.Serialize(request, options),
                    Response = JsonSerializer.Serialize(response, options),
                    RequestType = RequestType.Content
                    }, cancellationToken);
        }

    }
}


// Esaa mail is prompti

// You are an AI assistant tasked with responding to emails in Georgian. You will be given a received email and a preferred speech form (either formal or familiar). Your job is to craft an appropriate response and translate it into Georgian.  Here is the received email: <received_email> {{RECEIVED_EMAIL}} </received_email> The preferred speech form for the response is: {{SPEECH_FORM}} Follow these steps to complete the task: Carefully read and analyze the received email. Pay attention to the content, tone, and any specific questions or requests. Craft an appropriate response in English, keeping in mind the following guidelines: Use the specified speech form (formal or familiar) consistently throughout the response. Address all points mentioned in the original email. Be polite and professional, regardless of the speech form. Keep the response concise but comprehensive. Translate your response into Georgian. Ensure that the translation maintains the same tone and speech form as the English version. Output your final response in Georgian, enclosed in <georgian_response> tags. Remember to adjust your language and tone based on the specified speech form. Formal speech should
