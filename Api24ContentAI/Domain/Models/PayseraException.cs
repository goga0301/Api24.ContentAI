using System;

namespace Api24ContentAI.Domain.Models
{
    public class PayseraException : Exception
    {
        public PayseraException(string msg) : base(msg) {}
        public PayseraException(string msg, Exception innerException) : base(msg, innerException) {}
    }
}
