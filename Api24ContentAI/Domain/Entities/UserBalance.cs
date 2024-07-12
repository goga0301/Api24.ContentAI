using System.ComponentModel.DataAnnotations;

namespace Api24ContentAI.Domain.Entities
{
    public class UserBalance
    {
        [Key]
        public int Id { get; set; }
        public string UserId { get; set; }
        public User User { get; set; }
        public decimal Balance { get; set; }
    }
}
