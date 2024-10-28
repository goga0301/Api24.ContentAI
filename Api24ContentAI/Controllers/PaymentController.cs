using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Models;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPayseraService _payseraService;

        public PaymentController(IPayseraService payseraService)
        {
            _payseraService = payseraService;
        }

        [HttpPost("create")]
        public async Task<ActionResult<PaymentResponse>> CreatePayment([FromBody] PaymentRequest request)
        {
            try
            {
                var response = await _payseraService.CreatePaymentAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("callback")]
        public async Task<IActionResult> PayseraCallback()
        {
            try
            {
                bool isValid = await _payseraService.ValidateCallbackAsync(Request.Query);

                if (!isValid)
                {
                    return BadRequest(new { error = "Invalid callback" });
                }

                return Ok("OK");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}

