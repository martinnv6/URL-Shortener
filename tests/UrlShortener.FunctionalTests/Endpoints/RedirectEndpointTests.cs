using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.FunctionalTests.Endpoints;

public class RedirectEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _nonRedirectingClient;

    public RedirectEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _nonRedirectingClient = factory.CreateNonRedirectingClient();
    }

    [Fact]
    public async Task Redirect_WithValidShortCode_ReturnsFoundAndLocation()
    {
        // 1. Create a URL
        var request = new CreateUrlRequest("https://example.com/redirect-target", null);
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateUrlResponse>();
        var shortCode = result!.ShortCode;

        // 2. Perform GET redirect with non-redirecting client
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"/{shortCode}");
        requestMessage.Headers.Add("User-Agent", "FunctionalTest/1.0");
        requestMessage.Headers.Add("Referer", "https://test.example.com");

        var response = await _nonRedirectingClient.SendAsync(requestMessage);

        // 3. Assert HTTP 302 and Location
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/redirect-target", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Redirect_WithInvalidShortCode_ReturnsNotFound()
    {
        var response = await _nonRedirectingClient.GetAsync("/invalid-code");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
