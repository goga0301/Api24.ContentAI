using System;
using System.ComponentModel.DataAnnotations;

namespace Api24ContentAI.Domain.Entities
{
    public class BaseEntity
    {
        [Key]
        public Guid Id { get; set; }
    }
}
