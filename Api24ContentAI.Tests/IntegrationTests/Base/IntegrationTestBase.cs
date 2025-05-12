using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Api24ContentAI.Tests.IntegrationTests.Base
{
    public abstract class IntegrationTestBase : IClassFixture<TestWebApplicationFactory<Program>>
    {
        protected readonly HttpClient _client;
        protected readonly TestWebApplicationFactory<Program> _factory;

        protected IntegrationTestBase(TestWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }
    }
}

