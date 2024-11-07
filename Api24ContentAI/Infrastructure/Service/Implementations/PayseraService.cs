using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using EVP.WebToPay.ClientAPI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;


namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class PayseraService : IPayseraService
    {
        private readonly Client _payseraClient;
        private readonly PayseraOptions _options;
        private readonly ILogger<PayseraService> _logger;

        public PayseraService(IOptions<PayseraOptions> options, ILogger<PayseraService> logger)
        {
            _options = options.Value;
            _payseraClient = new Client(_options.ProjectId, _options.SignPassword);
            _logger = logger;
        }

        public async Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request)
        {
            ValidateRequest(request);

            var macroRequest = _payseraClient.NewMacroRequest();

            macroRequest.OrderId = request.OrderId;
            macroRequest.Amount = (int)(request.Amount * 100);
            macroRequest.Currency = request.Currency;
            macroRequest.Country = request.Country;
            macroRequest.AcceptUrl = _options.AcceptUrl;
            macroRequest.CancelUrl = _options.CancelUrl;
            macroRequest.CallbackUrl = _options.CallbackUrl;

            if (_options.IsTestMode)
            {
                macroRequest.Test = true;
            }

            string redirectUrl = _payseraClient.BuildRequestUrl(macroRequest);
            _logger.LogInformation("Payment request created for OrderId: {OrderId}", request.OrderId);


            return await Task.FromResult(new PaymentResponse
            {
                RedirectUrl = redirectUrl,
                OrderId = request.OrderId
            });
        }

        public async Task<PaymentStatus> ProcessCallbackAsync(Dictionary<string, string> parameters)
        {
            try
            {
                var status = parameters.GetValueOrDefault("status", "");
                var orderId = parameters.GetValueOrDefault("orderid", "");
                var paymentStatus = MapPaymentStatus(status);

                _logger.LogInformation("Processing payment callback for OrderId: {OrderId}, Status: {Status}",
                orderId, paymentStatus);

                return paymentStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment callback");
                throw new PayseraException("Failed to process payment callback", ex);
            }
        }

        public async Task<bool> ValidateCallbackAsync(Dictionary<string, string> parameters)
        {
            try
            {
                var signature = parameters.GetValueOrDefault("sign");
                var dataToSign = BuildSignatureString(parameters);
                var expectedSignature = GenerateSignature(dataToSign, _options.SignPassword);

                return signature == expectedSignature;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating callback signature");
                throw new PayseraException("Failed to validate callback signature", ex);
            }
        }

        private static string GenerateSignature(string data, string password)
        {
            using (var md5 = MD5.Create())
            {
                var inputBytes = Encoding.ASCII.GetBytes(data + password);
                var hashBytes = md5.ComputeHash(inputBytes);
                return Convert.ToHexString(hashBytes).ToLower();
            }
        }

        private static string BuildSignatureString(Dictionary<string, string> parameters)
        {
            var orderedParams = string.Join("&", parameters
                .Where(p => p.Key != "sign")
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}"));

            return orderedParams;
        }

        private static PaymentStatus MapPaymentStatus(string status)
            => status.ToLower() switch
            {
                "ok" => PaymentStatus.Completed,
                "pending" => PaymentStatus.Pending,
                "cancel" => PaymentStatus.Cancelled,
                _ => PaymentStatus.Failed
            };

        private static void ValidateRequest(PaymentRequest request)
        {
            if (string.IsNullOrEmpty(request.OrderId))
                throw new PayseraException("OrderId is required");

            if (request.Amount <= 0)
                throw new PayseraException("Amount must be greater than 0");

            if (string.IsNullOrEmpty(request.Currency))
                throw new PayseraException("Currency is required");
        }
    }
}

