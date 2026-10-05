using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.FunctionalTests.Endpoints;

public class CreateUrlEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CreateUrlEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateUrl_WithValidAbsoluteUrl_ReturnsCreated()
    {
        var request = new CreateUrlRequest("https://example.com/some/path", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(result);
        Assert.Equal("https://example.com/some/path", result.OriginalUrl);
        Assert.NotEmpty(result.ShortCode);
        Assert.NotEmpty(result.ShortUrl);
    }

    [Fact]
    public async Task CreateUrl_WithCustomAlias_ReturnsCreatedWithAlias()
    {
        var request = new CreateUrlRequest("https://example.com/another/path", "my-custom-alias");
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(result);
        Assert.Equal("my-custom-alias", result.ShortCode);
    }

    [Fact]
    public async Task CreateUrl_WithDuplicateCustomAlias_ReturnsConflict()
    {
        var alias = "duplicate-alias";
        var request1 = new CreateUrlRequest("https://example.com/1", alias);
        var request2 = new CreateUrlRequest("https://example.com/2", alias);

        var response1 = await _client.PostAsJsonAsync("/api/v1/urls", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        var response2 = await _client.PostAsJsonAsync("/api/v1/urls", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative/path")]
    public async Task CreateUrl_WithInvalidUrls_ReturnsBadRequest(string invalidUrl)
    {
        var request = new CreateUrlRequest(invalidUrl, null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
