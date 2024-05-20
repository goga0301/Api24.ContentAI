using System.ComponentModel.DataAnnotations;

namespace Api24ContentAI.Domain.Entities
{
    public class Language
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
