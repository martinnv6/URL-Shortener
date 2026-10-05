using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.FunctionalTests.Endpoints;

public class AnalyticsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _nonRedirectingClient;

    public AnalyticsEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _nonRedirectingClient = factory.CreateNonRedirectingClient();
    }

    [Fact]
    public async Task GetAnalytics_WithValidShortCodeAndNoClicks_ReturnsZero()
    {
        // 1. Create a URL
        var request = new CreateUrlRequest("https://example.com/analytics-zero", null);
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateUrlResponse>();
        var shortCode = result!.ShortCode;

        // 2. Get Analytics
        var response = await _client.GetAsync($"/api/v1/urls/{shortCode}/analytics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var analytics = await response.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();
        Assert.NotNull(analytics);
        Assert.Equal(0, analytics.TotalClicks);
        Assert.Empty(analytics.RecentClicks);
    }

    [Fact]
    public async Task GetAnalytics_WithInvalidShortCode_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/urls/invalid-code/analytics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_WithClicks_AggregatesCorrectlyAsync()
    {
        // 1. Create a URL
        var request = new CreateUrlRequest("https://example.com/analytics-clicks", "analytics-alias");
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateUrlResponse>();
        var shortCode = result!.ShortCode;

        // 2. Trigger clicks
        await TriggerClickAsync(shortCode, "IntegrationTestRunner/2.0", "https://github.com/martinnv6/URL-Shortener");
        await TriggerClickAsync(shortCode, "IntegrationTestRunner/2.0", "https://google.com");
        await TriggerClickAsync(shortCode, "AnotherClient/1.0", "https://google.com");
        await TriggerClickAsync(shortCode, "IntegrationTestRunner/2.0", "https://bing.com");

        // 3. Await background processing and assert
        await _factory.WaitForAnalyticsAsync(shortCode, analytics => analytics.TotalClicks == 4, TimeSpan.FromSeconds(3));

        // 4. Verify aggregated data
        var response = await _client.GetAsync($"/api/v1/urls/{shortCode}/analytics");
        var finalAnalytics = await response.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();

        Assert.NotNull(finalAnalytics);
        Assert.Equal(4, finalAnalytics.TotalClicks);

        // Verify recent clicks
        Assert.Equal(4, finalAnalytics.RecentClicks.Count);
        // The most recent click should be at index 0 (bing.com)
        Assert.Equal("https://bing.com/", finalAnalytics.RecentClicks[0].Referer);
        Assert.Equal("IntegrationTestRunner/2.0", finalAnalytics.RecentClicks[0].UserAgent);
    }

    private async Task TriggerClickAsync(string shortCode, string userAgent, string referer)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"/{shortCode}");
        requestMessage.Headers.Add("User-Agent", userAgent);
        requestMessage.Headers.Add("Referer", referer);
        var response = await _nonRedirectingClient.SendAsync(requestMessage);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }
}
