using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Security.Claims;
using Api24ContentAI.Domain.Repository;
using System.Threading;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _payseraService;
        private readonly ILogger<PaymentController> _logger;
        private readonly IUserService _userService;

        public PaymentController(IPaymentService payseraService,IUserService userService, ILogger<PaymentController> logger)
        {
            _payseraService = payseraService;
            _userService = userService;
            _logger = logger;
        }

        [HttpPost("create")]
        [Authorize]
        public async Task<ActionResult<PaymentResponse>> CreatePayment([FromBody] PaymentRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var response = await _payseraService.CreatePaymentAsync(request, userId);
                return Ok(response);
            }
            catch (PayseraException ex)
            {
                _logger.LogError(ex,"Paysera payment creation failed");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during payment creation");
                return StatusCode(500, new {error = "An unexpected error occurred"});
            }
        }

        [HttpPost("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleCallBack([FromForm] Dictionary<string, string> parameters)
        {
            try
            {
                var isValid = await _payseraService.ValidateCallbackAsync(parameters);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid callback signature received");
                    return BadRequest("Invalid signature");
                }

                var paymentStatus = await _payseraService.ProcessCallbackAsync(parameters);
                return Ok(paymentStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing callback");
                return StatusCode(500, "Error processing callback");
            }
        }
    }
}

