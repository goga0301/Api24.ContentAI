using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Authorization;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class LanguageController : Controller
    {
        private readonly ILanguageService _languageService;

        public LanguageController(ILanguageService languageService)
        {
            _languageService = languageService;
        }

        [HttpGet]
        public async Task<List<LanguageModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _languageService.GetAll(cancellationToken);
        }

        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAllPublic(CancellationToken cancellationToken)
        {
            try
            {
                var languages = await _languageService.GetAll(cancellationToken);
                return Ok(new { 
                    languages = languages,
                    count = languages.Count,
                    message = "Available language IDs for translation requests"
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<LanguageModel> GetById(int id, CancellationToken cancellationToken)
        {
            return await _languageService.GetById(id, cancellationToken);
        }

        [HttpPost]
        public async Task Create([FromBody] CreateLanguageModel model, CancellationToken cancellationToken)
        {
            await _languageService.Create(model, cancellationToken);
        }

        [HttpPost("seed-defaults")]
        [AllowAnonymous] // Temporarily allow anonymous access for seeding
        public async Task<IActionResult> SeedDefaultLanguages(CancellationToken cancellationToken)
        {
            try
            {
                // Check if languages already exist
                var existingLanguages = await _languageService.GetAll(cancellationToken);
                if (existingLanguages.Count > 0)
                {
                    return Ok(new { message = "Languages already exist", count = existingLanguages.Count });
                }

                // Create default languages
                var defaultLanguages = new List<CreateLanguageModel>
                {
                    new CreateLanguageModel { Name = "English", NameGeo = "ინგლისური" },
                    new CreateLanguageModel { Name = "Georgian", NameGeo = "ქართული" },
                    new CreateLanguageModel { Name = "German", NameGeo = "გერმანული" },
                    new CreateLanguageModel { Name = "French", NameGeo = "ფრანგული" },
                    new CreateLanguageModel { Name = "Spanish", NameGeo = "ესპანური" },
                    new CreateLanguageModel { Name = "Italian", NameGeo = "იტალიური" },
                    new CreateLanguageModel { Name = "Russian", NameGeo = "რუსული" },
                    new CreateLanguageModel { Name = "Dutch", NameGeo = "ნიდერლანდური" },
                    new CreateLanguageModel { Name = "Polish", NameGeo = "პოლონური" },
                    new CreateLanguageModel { Name = "Portuguese", NameGeo = "პორტუგალიური" }
                };

                foreach (var language in defaultLanguages)
                {
                    await _languageService.Create(language, cancellationToken);
                }

                return Ok(new { message = "Default languages created successfully", count = defaultLanguages.Count });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut]
        public async Task Update([FromBody] UpdateLanguageModel model, CancellationToken cancellationToken)
        {
            await _languageService.Update(model, cancellationToken);
        }

        [HttpDelete("{id}")]
        public async Task Delete(int id, CancellationToken cancellationToken)
        {
            await _languageService.Delete(id, cancellationToken);
        }
    }
}
