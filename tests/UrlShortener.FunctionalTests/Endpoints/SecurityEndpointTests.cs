using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.FunctionalTests.Endpoints;

public class SecurityEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── SSRF Rejection ─────────────────────────────────────────────

    [Fact]
    public async Task CreateUrl_WithLoopbackAddress_Returns400WithReasonCode()
    {
        var request = new CreateUrlRequest("http://127.0.0.1/admin", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("LOOPBACK_ADDRESS", body?.ReasonCode);
    }

    [Fact]
    public async Task CreateUrl_WithCloudMetadataEndpoint_Returns400WithReasonCode()
    {
        var request = new CreateUrlRequest("http://169.254.169.254/latest/meta-data/", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("LINK_LOCAL_ADDRESS", body?.ReasonCode);
    }

    [Fact]
    public async Task CreateUrl_WithPrivateNetworkAddress_Returns400WithReasonCode()
    {
        var request = new CreateUrlRequest("http://10.0.0.1", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("PRIVATE_NETWORK", body?.ReasonCode);
    }

    [Fact]
    public async Task CreateUrl_WithFtpScheme_Returns400WithReasonCode()
    {
        var request = new CreateUrlRequest("ftp://evil.com/payload", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("SCHEME_NOT_ALLOWED", body?.ReasonCode);
    }

    [Fact]
    public async Task CreateUrl_WithLocalhost_Returns400WithReasonCode()
    {
        var request = new CreateUrlRequest("http://localhost:8080", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("LOOPBACK_ADDRESS", body?.ReasonCode);
    }

    // ─── Custom Alias Validation ────────────────────────────────────

    [Fact]
    public async Task CreateUrl_WithInvalidAliasCharacters_Returns400()
    {
        var request = new CreateUrlRequest("https://example.com", "my alias!!");
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_ALIAS_CHARACTERS", body?.ReasonCode);
    }

    [Fact]
    public async Task CreateUrl_WithValidAlias_Returns201()
    {
        var request = new CreateUrlRequest("https://example.com/sec-valid", "sec-valid_1");
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ─── Alias Conflict ProblemDetails ───────────────────────────────

    [Fact]
    public async Task CreateUrl_DuplicateAlias_Returns409ProblemDetails()
    {
        var alias = "conflict-test";
        var request1 = new CreateUrlRequest("https://example.com/first", alias);
        var request2 = new CreateUrlRequest("https://example.com/second", alias);

        var response1 = await _client.PostAsJsonAsync("/api/v1/urls", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        var response2 = await _client.PostAsJsonAsync("/api/v1/urls", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        // RFC 7807 ProblemDetails should have title and detail
        var problemDetails = await response2.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.Equal("Alias Conflict", problemDetails?.Title);
        Assert.Contains(alias, problemDetails?.Detail ?? "");
        Assert.Equal(409, problemDetails?.Status);
    }

    // ─── Rate Limiting ──────────────────────────────────────────────
    // Rate limiter tests are in RateLimiterEndpointTests.cs
    // (separate class with its own factory to avoid request budget contention)

    /// <summary>
    /// Helper record for deserializing error responses.
    /// </summary>
    private record ErrorResponse(string? Error, string? ReasonCode);

    /// <summary>
    /// Helper record for deserializing RFC 7807 ProblemDetails responses.
    /// </summary>
    private record ProblemDetailsResponse(string? Title, string? Detail, int? Status);
}
