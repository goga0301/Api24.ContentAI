using System.Text.Json.Serialization;

namespace Api24ContentAI.Domain.Models
{
    public class CategoryResponse
    {

        [JsonPropertyName("id")]
        public string Id {  get; set; }
        
        [JsonPropertyName("name")]
        public string Name {  get; set; }

        [JsonPropertyName("originalName")]
        public string NameEng {  get; set; }
    }
}
