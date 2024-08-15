using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Api24ContentAI.Domain.Service;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailSenderController : ControllerBase
    {
        private readonly IEmailSenderService _emailSender;

        public EmailSenderController(IEmailSenderService emailSender)
        {
            _emailSender = emailSender;
        }

        [HttpPost("send-email")]
        public async Task<IActionResult> SendEmail(string email, string body, string subject, CancellationToken cancellationToken)
        {
            await _emailSender.SendEmailAsync(email, body, subject, cancellationToken);
            return Ok();
        }
    }
}
