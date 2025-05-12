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
        public string System { get; }
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; }
        [JsonPropertyName("temperature")]
        public decimal Temperature { get; }
        [JsonPropertyName("messages")]
        public List<MessageWithFile> Messages { get; }

        public ClaudeRequestWithFile(List<ContentFile> contents, string system = "")
        {
            Temperature = 0.5m;
            Model = "claude-3-7-sonnet-20250219";
            MaxTokens = 64000;
            Messages = new List<MessageWithFile> { new MessageWithFile("user", contents) };
            if (!string.IsNullOrWhiteSpace(system))
            {
                System = system;
            }
        }

        public ClaudeRequestWithFile(List<MessageWithFile> messages)
        {
            Model = "claude-3-7-sonnet-20250219";
            MaxTokens = 4096;
            Messages = messages;
        }
    }

    public class ClaudeRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; } = "claude-3-5-sonnet-20240620";
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; }
        [JsonPropertyName("messages")]
        public List<Message> Messages { get; } = [new Message("user", messageContent)];
    }
}
