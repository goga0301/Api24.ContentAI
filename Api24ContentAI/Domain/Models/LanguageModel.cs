namespace Api24ContentAI.Domain.Models
{
    public class LanguageModel
    {
        public int Id { get; set; }
        public string Name { get; set; }    
    }

    public class CreateLanguageModel
    {
        public string Name { get; set; }
    }

    public class UpdateLanguageModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
