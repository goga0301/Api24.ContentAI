using Api24ContentAI.Tests.IntegrationTests.Base;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using Api24ContentAI.Domain.Models;
using System.IO;
using System;

namespace Api24ContentAI.Tests.IntegrationTests.Controllers
{
    public class UserContentControllerTest : IntegrationTestBase
    {
        public UserContentControllerTest(TestWebApplicationFactory<Program> factory) : base(factory)
        {
        }

        [Fact]
        public async Task Translate_WithValidRequest_ShouldReturnSuccess()
        {
            var loginRequest = new LoginRequest
            {
                UserName = "matekopaliani12@gmail.com",
                Password = "string"
            };
            
            var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login", loginRequest);
            loginResponse.EnsureSuccessStatusCode();
            
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginResult);
            Assert.NotNull(loginResult.Token);
            
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
            
            using var content = new MultipartFormDataContent();
            
            content.Add(new StringContent("This is a test text to translate."), "Description");
            content.Add(new StringContent("2"), "LanguageId"); 
            content.Add(new StringContent("1"), "SourceLanguageId"); 
            content.Add(new StringContent("false"), "IsPdf");
            
            var response = await _client.PostAsync("/api/UserContent/translate", content);
            response.EnsureSuccessStatusCode();
            
            var translateResult = await response.Content.ReadFromJsonAsync<TranslateResponse>();
            Assert.NotNull(translateResult);
            Assert.NotNull(translateResult.Text);
        }

        [Fact]
        public async Task Translate_WithoutAuthentication_ShouldReturnUnauthorized()
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("Test text"), "Description");
            content.Add(new StringContent("2"), "LanguageId");
            content.Add(new StringContent("1"), "SourceLanguageId");
            content.Add(new StringContent("false"), "IsPdf");
            
            var response = await _client.PostAsync("/api/UserContent/translate", content);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Translate_WithInvalidLanguageId_ShouldReturnBadRequest()
        {
            var loginRequest = new LoginRequest
            {
                UserName = "matekopaliani12@gmail.com",
                Password = "string"
            };
            
            var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login", loginRequest);
            loginResponse.EnsureSuccessStatusCode();
            
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
            
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("Test text"), "Description");
            content.Add(new StringContent("999"), "LanguageId"); 
            content.Add(new StringContent("2"), "SourceLanguageId");
            content.Add(new StringContent("false"), "IsPdf");
            
            var response = await _client.PostAsync("/api/UserContent/translate", content);
            
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Copyright_WithValidImage_ShouldReturnGeneratedText()
        {
            var loginRequest = new LoginRequest
            {
                UserName = "matekopaliani12@gmail.com",
                Password = "string"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login", loginRequest);
            loginResponse.EnsureSuccessStatusCode();

            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginResult);
            Assert.NotNull(loginResult.Token);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

            string projectRoot = AppDomain.CurrentDomain.BaseDirectory;
            string imagePath = Path.Combine(projectRoot, "IntegrationTests", "Controllers", "images");
            string testImagePath = Path.Combine(imagePath, "hello_world.png");
            


            if (!File.Exists(testImagePath))
            {
                throw new FileNotFoundException("The specified test image does not exist.", testImagePath);
            }

            using var content = new MultipartFormDataContent();

            using var fileStream = new FileStream(testImagePath, FileMode.Open, FileAccess.Read);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", "test_copyright_image.png");

            content.Add(new StringContent("2"), "LanguageId");
            content.Add(new StringContent("Test Product"), "ProductName");

            var response = await _client.PostAsync("/api/UserContent/copyright", content);

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var errorObj = JsonSerializer.Deserialize<Error>(responseContent);
                    Assert.Fail($"Copyright failed: {errorObj.ErrorText}");
                }
                catch
                {
                    Assert.Fail($"Copyright failed with status {response.StatusCode}: {responseContent}");
                }
            }

            var copyrightResult = JsonSerializer.Deserialize<CopyrightAIResponse>(
                responseContent,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            Assert.NotNull(copyrightResult);
            Assert.NotNull(copyrightResult.Text);
        }
    }
}