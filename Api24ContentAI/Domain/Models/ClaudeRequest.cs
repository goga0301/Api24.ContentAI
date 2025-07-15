using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Api24ContentAI.Domain.Models
{

    public class Message(string role, string content)
    {
        [JsonPropertyName("role")]
        public string Role { get; } = role;
        [JsonPropertyName("content")]
        public string Content { get; } = content;
    }

    public class MessageWithFile(string role, List<ContentFile> content)
    {
        [JsonPropertyName("role")]
        public string Role { get; } = role;
        [JsonPropertyName("content")]
        public List<ContentFile> Content { get; set; } = content;
    }

    public class ContentFile
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("source")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Source? Source { get; set; }
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }
        [JsonPropertyName("cache_control")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CacheControl? CacheControl { get; set; }
    }

    public class CacheControl
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "ephemeral";
    }

    public class Source
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("media_type")]
        public string MediaType { get; set; }
        [JsonPropertyName("data")]
        public string Data { get; set; }
    }


    public class ClaudeRequestWithFile
    {
        [JsonPropertyName("model")]
        public string Model { get; }
        [JsonPropertyName("system")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SystemMessage>? System { get; set; }
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; }
        [JsonPropertyName("temperature")]
        public decimal Temperature { get; }
        [JsonPropertyName("messages")]
        public List<MessageWithFile> Messages { get; }

        public ClaudeRequestWithFile(List<ContentFile> contents, string system = "", bool cacheSystemPrompt = false, string model = "claude-4-sonnet-20250514")
        {
            Temperature = 0.5m;
            Model = model;
            MaxTokens = 64000;
            Messages = new List<MessageWithFile> { new MessageWithFile("user", contents) };
            if (!string.IsNullOrWhiteSpace(system))
            {
                System = new List<SystemMessage>
                {
                    new SystemMessage
                    {
                        Type = "text",
                        Text = system,
                        CacheControl = cacheSystemPrompt ? new CacheControl() : null
                    }
                };
            }
        }

        public ClaudeRequestWithFile(List<MessageWithFile> messages, string model = "claude-4-sonnet-20250514")
        {
            Model = model;
            MaxTokens = 4096;
            Messages = messages;
        }

        public ClaudeRequestWithFile(List<ContentFile> contents, List<SystemMessage> systemMessages, string model = "claude-4-sonnet-20250514")
        {
            Temperature = 0.5m;
            Model = model;
            MaxTokens = 64000;
            Messages = new List<MessageWithFile> { new MessageWithFile("user", contents) };
            System = systemMessages;
        }
    }

    public class SystemMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";
        [JsonPropertyName("text")]
        public string Text { get; set; }
        [JsonPropertyName("cache_control")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CacheControl? CacheControl { get; set; }
    }

    public class ClaudeRequest(string messageContent, string model = "claude-3-7-sonnet-20250219")
    {
        [JsonPropertyName("model")]
        public string Model { get; } = model;
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; } = 4096;
        [JsonPropertyName("messages")]
        public List<Message> Messages { get; } = [new Message("user", messageContent)];
    }

    public class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; }
        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig GenerationConfig { get; set; }

        public GeminiRequest(string messageContent)
        {
            Contents = new List<GeminiContent>
            {
                new GeminiContent
                {
                    Parts = new List<GeminiPart>
                    {
                        new GeminiPart { Text = messageContent }
                    }
                }
            };
            GenerationConfig = new GeminiGenerationConfig();
        }

        public GeminiRequest(List<GeminiPart> parts)
        {
            Contents = new List<GeminiContent>
            {
                new GeminiContent { Parts = parts }
            };
            GenerationConfig = new GeminiGenerationConfig();
        }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; }
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("inlineData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiInlineData? InlineData { get; set; }
    }

    public class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }

        [JsonPropertyName("data")]
        public string Data { get; set; }
    }

    public class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; } = 0.3m;

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; } = 16384;
    }

    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate> Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string FinishReason { get; set; }
    }
}
