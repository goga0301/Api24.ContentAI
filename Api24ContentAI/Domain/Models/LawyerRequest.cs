using System;

namespace Api24ContentAI.Domain.Models
{
    public class LawyerRequest
    {
        public string Prompt { get; set; }
        public Guid UniqueKey { get; set; }
    }
}
