using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api24ContentAI.Tests.IntegrationTests.Base
{
    public class TestWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // No need to modify anything - use the real services and database
            // The test will use your actual PostgreSQL database
        }
    }
}
