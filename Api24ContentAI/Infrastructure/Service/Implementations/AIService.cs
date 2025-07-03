using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class AIService : IAIService
    {
        private readonly IClaudeService _claudeService;
        private readonly IGeminiService _geminiService;
        private readonly ILogger<AIService> _logger;

        public AIService(IClaudeService claudeService, IGeminiService geminiService, ILogger<AIService> logger)
        {
            _claudeService = claudeService ?? throw new ArgumentNullException(nameof(claudeService));
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AIResponse> SendTextRequest(string prompt, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Sending text request to {Model}", model);

                switch (model)
                {
                    case AIModel.Claude4Sonnet:
                    case AIModel.Claude37Sonnet:
                        return await SendClaudeTextRequest(prompt, model, cancellationToken);
                    
                    case AIModel.Gemini25Pro:
                        return await SendGeminiTextRequest(prompt, cancellationToken);
                    
                    default:
                        throw new ArgumentException($"Unsupported AI model: {model}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendTextRequest for model {Model}", model);
                return new AIResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    UsedModel = model
                };
            }
        }

        public async Task<AIResponse> SendRequestWithImages(string prompt, List<AIImageData> images, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Sending request with {ImageCount} images to {Model}", images.Count, model);

                switch (model)
                {
                    case AIModel.Claude4Sonnet:
                    case AIModel.Claude37Sonnet:
                        return await SendClaudeRequestWithImages(prompt, images, model, cancellationToken);
                    
                    case AIModel.Gemini25Pro:
                        return await SendGeminiRequestWithImages(prompt, images, cancellationToken);
                    
                    default:
                        throw new ArgumentException($"Unsupported AI model: {model}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendRequestWithImages for model {Model}", model);
                return new AIResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    UsedModel = model
                };
            }
        }

        public async Task<AIResponse> SendRequestWithFile(List<ContentFile> contentFiles, AIModel model, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Sending request with {FileCount} content files to {Model}", contentFiles.Count, model);

                switch (model)
                {
                    case AIModel.Claude4Sonnet:
                    case AIModel.Claude37Sonnet:
                        return await SendClaudeRequestWithFiles(contentFiles, model, cancellationToken);
                    
                    case AIModel.Gemini25Pro:
                        return await SendGeminiRequestWithFiles(contentFiles, cancellationToken);
                    
                    default:
                        throw new ArgumentException($"Unsupported AI model: {model}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendRequestWithFile for model {Model}", model);
                return new AIResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    UsedModel = model
                };
            }
        }

        private async Task<AIResponse> SendClaudeTextRequest(string prompt, AIModel model, CancellationToken cancellationToken)
        {
            var modelString = GetClaudeModelString(model);
            var claudeRequest = new ClaudeRequest(prompt, modelString);
            
            var response = await _claudeService.SendRequest(claudeRequest, cancellationToken);
            
            return new AIResponse
            {
                Success = true,
                Content = response.Content?.FirstOrDefault()?.Text ?? string.Empty,
                UsedModel = model
            };
        }

        private async Task<AIResponse> SendClaudeRequestWithImages(string prompt, List<AIImageData> images, AIModel model, CancellationToken cancellationToken)
        {
            var contentFiles = new List<ContentFile>
            {
                new ContentFile { Type = "text", Text = prompt }
            };

            foreach (var image in images)
            {
                contentFiles.Add(new ContentFile
                {
                    Type = "image",
                    Source = new Source
                    {
                        Type = "base64",
                        MediaType = image.MimeType,
                        Data = image.Base64Data
                    }
                });
            }
            var modelString = GetClaudeModelString(model);
            var claudeRequest = new ClaudeRequestWithFile(contentFiles, model: modelString);
            var response = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            return new AIResponse
            {
                Success = true,
                Content = response.Content?.FirstOrDefault()?.Text ?? string.Empty,
                UsedModel = model
            };
        }

        private async Task<AIResponse> SendClaudeRequestWithFiles(List<ContentFile> contentFiles, AIModel model, CancellationToken cancellationToken)
        {
            var modelString = GetClaudeModelString(model);
            var claudeRequest = new ClaudeRequestWithFile(contentFiles, model: modelString);
            var response = await _claudeService.SendRequestWithFile(claudeRequest, cancellationToken);
            
            return new AIResponse
            {
                Success = true,
                Content = response.Content?.FirstOrDefault()?.Text ?? string.Empty,
                UsedModel = model
            };
        }

        private async Task<AIResponse> SendGeminiTextRequest(string prompt, CancellationToken cancellationToken)
        {
            var geminiRequest = new GeminiRequest(prompt);
            var response = await _geminiService.SendRequest(geminiRequest, cancellationToken);
            
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var cleanedContent = CleanGeminiHtmlResponse(content);
            
            return new AIResponse
            {
                Success = true,
                Content = cleanedContent,
                UsedModel = AIModel.Gemini25Pro
            };
        }

        private async Task<AIResponse> SendGeminiRequestWithImages(string prompt, List<AIImageData> images, CancellationToken cancellationToken)
        {
            var parts = new List<GeminiPart>
            {
                new GeminiPart { Text = prompt }
            };

            foreach (var image in images)
            {
                parts.Add(new GeminiPart
                {
                    InlineData = new GeminiInlineData
                    {
                        MimeType = image.MimeType,
                        Data = image.Base64Data
                    }
                });
            }

            var response = await _geminiService.SendRequestWithFile(parts, cancellationToken);
            
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var cleanedContent = CleanGeminiHtmlResponse(content);
            
            return new AIResponse
            {
                Success = true,
                Content = cleanedContent,
                UsedModel = AIModel.Gemini25Pro
            };
        }

        private async Task<AIResponse> SendGeminiRequestWithFiles(List<ContentFile> contentFiles, CancellationToken cancellationToken)
        {
            var parts = new List<GeminiPart>();

            foreach (var contentFile in contentFiles)
            {
                if (contentFile.Type == "text")
                {
                    parts.Add(new GeminiPart { Text = contentFile.Text });
                }
                else if (contentFile.Type == "image" && contentFile.Source != null)
                {
                    parts.Add(new GeminiPart
                    {
                        InlineData = new GeminiInlineData
                        {
                            MimeType = contentFile.Source.MediaType,
                            Data = contentFile.Source.Data
                        }
                    });
                }
            }

            var response = await _geminiService.SendRequestWithFile(parts, cancellationToken);
            
            var content = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
            var cleanedContent = CleanGeminiHtmlResponse(content);
            
            return new AIResponse
            {
                Success = true,
                Content = cleanedContent,
                UsedModel = AIModel.Gemini25Pro
            };
        }

        private static string CleanGeminiHtmlResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            var htmlBlockPattern = @"^```(?:html)?\s*\n(.*?)\n```\s*$";
            var match = System.Text.RegularExpressions.Regex.Match(content, htmlBlockPattern, 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            var multipleBlockPattern = @"```(?:html)?\s*\n(.*?)\n```";
            var firstMatch = System.Text.RegularExpressions.Regex.Match(content, multipleBlockPattern, 
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (firstMatch.Success)
            {
                return firstMatch.Groups[1].Value.Trim();
            }

            return content;
        }

        private string GetClaudeModelString(AIModel model)
        {
            return model switch
            {
                AIModel.Claude4Sonnet => "claude-sonnet-4-20250514",
                AIModel.Claude37Sonnet => "claude-3-7-sonnet-20250219",
                _ => "claude-3-haiku-20240307"
            };
        }
    }
} 
