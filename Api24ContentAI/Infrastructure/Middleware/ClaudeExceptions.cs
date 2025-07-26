using System;

namespace Api24ContentAI.Infrastructure.Middleware {
    public class ClaudeApiException : Exception
    {
        public int? StatusCode { get; }
        public string ResponseContent { get; }
        public string ClientMessage { get; }

        public ClaudeApiException(string message, int? statusCode = null, string responseContent = null, string clientMessage = null, Exception inner = null)
            : base(message, inner)
            {
                StatusCode = statusCode;
                ResponseContent = responseContent;
                ClientMessage = clientMessage ?? "An error occurred while translating your document. Please try again.";
            }
    }



    public class ClaudeRateLimitException : ClaudeApiException
    {
        public ClaudeRateLimitException(string message, string responseContent = null)
            : base(message, 429, responseContent) { }
    }


}
