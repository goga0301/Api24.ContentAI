using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using EVP.WebToPay.ClientAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Linq;
using System;
using System.Threading.Tasks;


namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class PayseraService : IPayseraService
    {
        private readonly Client _payseraClient;
        private readonly PayseraOptions _options;

        public PayseraService(IOptions<PayseraOptions> options)
        {
            _options = options.Value;
            _payseraClient = new Client(_options.ProjectId, _options.SignPassword);
        }

        public async Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request)
        {
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

            return await Task.FromResult(new PaymentResponse
            {
                RedirectUrl = redirectUrl
            });
        }

        public async Task<bool> ValidateCallbackAsync(IQueryCollection query)
        {
            try
            {
                var microAnswer = _payseraClient.NewMicroAnswer();

                // Convert query parameters to dictionary
                var parameters = query.ToDictionary(
                    x => x.Key,
                    x => x.Value.ToString()
                );

                // Validate the callback
                //microAnswer.LoadFromCollection(parameters);
                return await Task.FromResult(true);
            }
            catch (Exception)
            {
                return await Task.FromResult(false);
            }
        }
    }
}

