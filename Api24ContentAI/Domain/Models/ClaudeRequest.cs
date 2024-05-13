using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Api24ContentAI.Domain.Models
{

    public class Message
    {
        [JsonPropertyName("role")]
        public string Role { get; }
        [JsonPropertyName("content")]
        public string Content { get; }

        public Message(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public class ClaudeRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; }
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get;  }
        [JsonPropertyName("messages")]
        public List<Message> Messages { get; }

        public ClaudeRequest(string messageContent)
        {
            Model = "claude-3-opus-20240229";
            MaxTokens = 2048;
            Messages = new List<Message> { new Message("user", messageContent) };
        }
    }
}
