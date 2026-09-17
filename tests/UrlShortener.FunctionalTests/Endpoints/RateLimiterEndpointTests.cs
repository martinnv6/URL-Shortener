using System.Net;
using Xunit;

namespace UrlShortener.FunctionalTests.Endpoints;

/// <summary>
/// Dedicated test class for rate limiter with its own factory instance,
/// isolated from other test classes to avoid consuming their request budget.
/// Uses a separate CustomWebApplicationFactory to get a fresh rate limiter state.
/// </summary>
public class RateLimiterEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RateLimiterEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RateLimiter_ExceedingLimit_Returns429WithRetryAfter()
    {
        // The middleware uses 30 req/min by default.
        // All requests from this test class come from the same loopback IP.
        HttpResponseMessage? rateLimitedResponse = null;

        for (int i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync($"/rate-limit-probe-{i}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        Assert.NotNull(rateLimitedResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse!.StatusCode);
        Assert.True(rateLimitedResponse.Headers.Contains("Retry-After"),
            "Response should contain Retry-After header.");
    }
}
