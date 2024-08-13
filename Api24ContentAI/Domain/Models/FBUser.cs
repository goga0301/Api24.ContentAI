using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Api24ContentAI.Domain.Models
{
    public class FBUser
    {
        [JsonPropertyName("data")]
        public Data Data { get; set; }
    }

    public class Data
    {
        [JsonPropertyName("app_id")]
        public long AppId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("application")]
        public string Application { get; set; }

        [JsonPropertyName("expires_at")]
        public long ExpiresAt { get; set; }

        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("issued_at")]
        public long IssuedAt { get; set; }

        [JsonPropertyName("metadata")]
        public Metadata Metadata { get; set; }

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }
    }

    public class Metadata
    {
        [JsonPropertyName("sso")]
        public string Sso { get; set; }
    }

    public class FBUserInfo
    {
        [JsonProperty("first_name")]
        public string FirstName { get; set; }

        [JsonProperty("last_name")]
        public string LastName { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }


}
