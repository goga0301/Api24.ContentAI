using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using SelectPdf;


namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserContentController : ControllerBase
    {
        private readonly IUserContentService _userContentService;
        public UserContentController(IUserContentService userContentService)
        {
            _userContentService = userContentService;
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] UserContentAIRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.SendRequest(request, userId, cancellationToken));
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromForm] UserTranslateRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                var result = await _userContentService.ChunkedTranslate(request, userId, cancellationToken);
                if (request.IsPdf)
                {
                    return File(fileContents: result.File,
                                contentType: "application/pdf",
                                fileDownloadName: "translated-document.pdf"
                            );
                }
                return Ok(result);

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("e-translate")]
        public async Task<IActionResult> TestTranslate(IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var htmlString = await _userContentService.TestTranslateTextAsync(file, cancellationToken);

                //---------------
                // Convert HTML to PDF using IronPDF
                //var renderer = new IronPdf.ChromePdfRenderer();
                //var pdf = renderer.RenderHtmlAsPdf(htmlString);
                //byte[] pdfBytes = pdf.BinaryData;

                //---------------
                //    var converter = new SynchronizedConverter(new PdfTools());

                //    // Configure PDF settings
                //    var doc = new HtmlToPdfDocument()
                //    {
                //        GlobalSettings = {
                //    ColorMode = ColorMode.Color,
                //    Orientation = Orientation.Portrait,
                //    PaperSize = PaperKind.A4,
                //    Margins = new MarginSettings { Top = 10, Bottom = 10, Left = 10, Right = 10 }
                //},
                //        Objects = {
                //    new ObjectSettings() {
                //        HtmlContent = htmlString,
                //        WebSettings = { DefaultEncoding = "utf-8" }
                //    }
                //}
                //    };
                //    // Get the PDF as a byte array
                //    byte[] pdfBytes = converter.Convert(doc);
                HtmlToPdf converter = new HtmlToPdf();

                // Configure PDF settings
                converter.Options.PdfPageSize = PdfPageSize.A4;
                converter.Options.PdfPageOrientation = PdfPageOrientation.Portrait;
                converter.Options.MarginLeft = 10;
                converter.Options.MarginRight = 10;
                converter.Options.MarginTop = 10;
                converter.Options.MarginBottom = 10;

                // Convert HTML to PDF
                PdfDocument pdfDocument = converter.ConvertHtmlString(htmlString);

                // Get PDF as bytes
                byte[] pdfBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    pdfDocument.Save(ms);
                    pdfBytes = ms.ToArray();
                }

                // Clean up
                pdfDocument.Close();

                // Return the PDF file
                return File(
                    fileContents: pdfBytes,
                    contentType: "application/pdf",
                    fileDownloadName: "translated-document.pdf"
                );
            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("enhance-translate")]
        public async Task<IActionResult> EnhanceTranslate([FromForm] UserTranslateEnhanceRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                var result = await _userContentService.EnhanceTranslate(request, userId, cancellationToken);
                return Ok(result);

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("copyright")]
        public async Task<IActionResult> Copyright([FromForm] UserCopyrightAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.CopyrightAI(file, request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("email")]
        public async Task<IActionResult> Email([FromBody] UserEmailRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.Email(request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

        [HttpPost("video-script")]
        public async Task<IActionResult> VideoScript([FromForm] UserVideoScriptAIRequest request, IFormFile file, CancellationToken cancellationToken)
        {
            try
            {
                var userId = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in the token");
                }
                return Ok(await _userContentService.VideoScript(file, request, userId, cancellationToken));

            }
            catch (Exception ex)
            {
                return BadRequest(new Error { ErrorText = ex.Message });
            }
        }

    }
}
