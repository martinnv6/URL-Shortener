using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Contracts;
using UrlShortener.Infrastructure.Persistence;
using Xunit;

namespace UrlShortener.IntegrationTests;

// ═══════════════════════════════════════════════════════════════════
//  Test Infrastructure — Isolated SQLite per test fixture
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Boots the full ASP.NET Core request pipeline using
/// <see cref="WebApplicationFactory{TEntryPoint}"/>.
///
/// CRITICAL: Each fixture provisions a unique, GUID-based SQLite database file
/// to prevent state pollution and race conditions during parallel xUnit execution.
/// </summary>
public class IsolatedWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private readonly string _dbFileName;
    private readonly string _connectionString;

    public IsolatedWebApplicationFactory()
    {
        _dbFileName = $"urlshortener.integrationtests.{Guid.NewGuid()}.db";
        _connectionString = $"Data Source={_dbFileName}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the production DbContext registration.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            var dbConnectionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbConnection));
            if (dbConnectionDescriptor != null) services.Remove(dbConnectionDescriptor);

            // Provision isolated SQLite connection.
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            services.AddDbContext<AppDbContext>((_, options) =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    public async Task InitializeAsync()
    {
        if (File.Exists(_dbFileName)) File.Delete(_dbFileName);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }

        if (File.Exists(_dbFileName)) File.Delete(_dbFileName);
        if (File.Exists(_dbFileName + "-shm")) File.Delete(_dbFileName + "-shm");
        if (File.Exists(_dbFileName + "-wal")) File.Delete(_dbFileName + "-wal");

        await base.DisposeAsync();
    }

    /// <summary>
    /// Creates an HttpClient that does NOT follow redirects,
    /// allowing assertions on HTTP 302 status and Location headers.
    /// </summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Polls the analytics endpoint until a predicate is satisfied or timeout expires.
    /// Handles eventual consistency from the background AnalyticsProcessingWorker.
    /// </summary>
    public async Task WaitForAnalyticsAsync(
        string shortCode,
        Func<ClickAnalyticsResponse, bool> condition,
        TimeSpan timeout)
    {
        using var client = CreateNonRedirectingClient();
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var response = await client.GetAsync($"/api/v1/urls/{shortCode}/analytics");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();
                if (result != null && condition(result))
                    return;
            }
            // Longer poll interval to minimize request count against rate limiter.
            await Task.Delay(200);
        }

        throw new TimeoutException($"Condition was not met within {timeout.TotalSeconds} seconds.");
    }
}

// ═══════════════════════════════════════════════════════════════════
//  SECTION 1: Happy Paths
//  (Isolated factory — own rate limiter budget)
// ═══════════════════════════════════════════════════════════════════

public class HappyPathTests : IClassFixture<IsolatedWebApplicationFactory>
{
    private readonly IsolatedWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _nonRedirectingClient;

    public HappyPathTests(IsolatedWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _nonRedirectingClient = factory.CreateNonRedirectingClient();
    }

    [Fact]
    public async Task CreateUrl_WithAutoGeneratedBase62Code_Returns201()
    {
        var request = new CreateUrlRequest("https://www.schwab.com/research", null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(result);
        Assert.Equal("https://www.schwab.com/research", result.OriginalUrl);
        Assert.NotEmpty(result.ShortCode);
        Assert.NotEmpty(result.ShortUrl);
        Assert.Contains(result.ShortCode, result.ShortUrl);
    }

    [Fact]
    public async Task CreateUrl_WithCustomAlias_Returns201WithExactAlias()
    {
        var request = new CreateUrlRequest("https://www.schwab.com/pricing", "schwab-pricing");
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateUrlResponse>();
        Assert.NotNull(result);
        Assert.Equal("schwab-pricing", result.ShortCode);
    }

    [Fact]
    public async Task Redirect_Returns302WithExactLocationHeader()
    {
        // 1. Create
        var original = "https://www.schwab.com/investing";
        var createResp = await _client.PostAsJsonAsync("/api/v1/urls",
            new CreateUrlRequest(original, null));
        var created = await createResp.Content.ReadFromJsonAsync<CreateUrlResponse>();

        // 2. Redirect (non-following client)
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/{created!.ShortCode}");
        req.Headers.Add("User-Agent", "IntegrationTest/1.0");
        req.Headers.Add("Referer", "https://test.com");

        var redirectResp = await _nonRedirectingClient.SendAsync(req);

        // 3. Assert exact 302 + Location
        Assert.Equal(HttpStatusCode.Found, redirectResp.StatusCode);
        Assert.Equal(original, redirectResp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Analytics_ReflectsAccurateCountAfter3Redirects()
    {
        // 1. Create
        var createResp = await _client.PostAsJsonAsync("/api/v1/urls",
            new CreateUrlRequest("https://www.schwab.com/brokerage", "analytics-3x"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var shortCode = "analytics-3x";

        // 2. Trigger exactly 3 redirect calls
        await TriggerClickAsync(shortCode, "TestAgent/1.0", "https://google.com");
        await TriggerClickAsync(shortCode, "TestAgent/1.0", "https://bing.com");
        await TriggerClickAsync(shortCode, "OtherAgent/2.0", "https://github.com");

        // 3. Wait for background worker to flush
        await _factory.WaitForAnalyticsAsync(
            shortCode,
            analytics => analytics.TotalClicks == 3,
            TimeSpan.FromSeconds(5));

        // 4. Verify count
        var analyticsResp = await _client.GetAsync($"/api/v1/urls/{shortCode}/analytics");
        var analytics = await analyticsResp.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();

        Assert.NotNull(analytics);
        Assert.Equal(3, analytics.TotalClicks);
        Assert.Equal(3, analytics.RecentClicks.Count);
    }

    [Fact]
    public async Task NonExistentShortCode_Returns404()
    {
        var response = await _nonRedirectingClient.GetAsync("/absolutely-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonExistentAnalytics_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/urls/nonexistent/analytics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task TriggerClickAsync(string shortCode, string userAgent, string referer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/{shortCode}");
        req.Headers.Add("User-Agent", userAgent);
        req.Headers.Add("Referer", referer);
        var resp = await _nonRedirectingClient.SendAsync(req);
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  SECTION 2: Adversarial — Malformed URLs & SSRF (OWASP API7:2023)
//  (Isolated factory — own rate limiter budget)
// ═══════════════════════════════════════════════════════════════════

public class AdversarialValidationTests : IClassFixture<IsolatedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AdversarialValidationTests(IsolatedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Malformed URLs ─────────────────────────────────────────────

    [Theory]
    [InlineData("")]                           // Empty
    [InlineData("   ")]                        // Whitespace only
    [InlineData("not-a-url")]                  // No scheme
    [InlineData("/relative/path")]             // Relative path
    [InlineData("://missing-scheme.com")]      // Missing scheme
    public async Task MalformedUrl_Returns400BadRequest(string malformedUrl)
    {
        var request = new CreateUrlRequest(malformedUrl, null);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── SSRF: Loopback ─────────────────────────────────────────────

    [Theory]
    [InlineData("http://127.0.0.1",            "LOOPBACK_ADDRESS")]
    [InlineData("http://127.0.0.1:8080/admin", "LOOPBACK_ADDRESS")]
    [InlineData("http://localhost",             "LOOPBACK_ADDRESS")]
    [InlineData("http://localhost:3000",        "LOOPBACK_ADDRESS")]
    [InlineData("http://[::1]",                "LOOPBACK_ADDRESS")]
    public async Task SSRF_LoopbackAddresses_Returns400(string url, string expectedCode)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/urls", new CreateUrlRequest(url, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(expectedCode, body?.ReasonCode);
    }

    // ─── SSRF: Private Networks ─────────────────────────────────────

    [Theory]
    [InlineData("http://10.0.0.1",         "PRIVATE_NETWORK")]
    [InlineData("http://172.16.0.1",       "PRIVATE_NETWORK")]
    [InlineData("http://192.168.1.1",      "PRIVATE_NETWORK")]
    public async Task SSRF_PrivateNetworks_Returns400(string url, string expectedCode)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/urls", new CreateUrlRequest(url, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(expectedCode, body?.ReasonCode);
    }

    // ─── SSRF: Cloud Metadata ───────────────────────────────────────

    [Theory]
    [InlineData("http://169.254.169.254",                     "LINK_LOCAL_ADDRESS")]
    [InlineData("http://169.254.169.254/latest/meta-data/",   "LINK_LOCAL_ADDRESS")]
    public async Task SSRF_CloudMetadata_Returns400(string url, string expectedCode)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/urls", new CreateUrlRequest(url, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(expectedCode, body?.ReasonCode);
    }

    // ─── SSRF: Forbidden Schemes ────────────────────────────────────

    [Theory]
    [InlineData("ftp://evil.com/payload",    "SCHEME_NOT_ALLOWED")]
    [InlineData("file:///etc/passwd",        "SCHEME_NOT_ALLOWED")]
    [InlineData("gopher://evil.com",         "SCHEME_NOT_ALLOWED")]
    public async Task SSRF_ForbiddenSchemes_Returns400(string url, string expectedCode)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/urls", new CreateUrlRequest(url, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(expectedCode, body?.ReasonCode);
    }

    // ─── Custom Alias Validation ────────────────────────────────────

    [Theory]
    [InlineData("my alias")]                   // Spaces
    [InlineData("my.alias")]                   // Dots
    [InlineData("my@alias")]                   // Special characters
    [InlineData("my/alias")]                   // Path separator
    [InlineData("DROP TABLE urls")]            // SQL injection attempt
    [InlineData("<script>alert(1)</script>")]   // XSS attempt
    public async Task IllegalAliasCharacters_Returns400(string alias)
    {
        var request = new CreateUrlRequest("https://example.com", alias);
        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record ErrorResponse(string? Error, string? ReasonCode);
}

// ═══════════════════════════════════════════════════════════════════
//  SECTION 3: Alias Collision & Concurrency
//  (Isolated factory — own rate limiter budget)
// ═══════════════════════════════════════════════════════════════════

public class AliasCollisionTests : IClassFixture<IsolatedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AliasCollisionTests(IsolatedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DuplicateAlias_Returns409ConflictWithProblemDetails()
    {
        var alias = "collision-1";
        var req1 = new CreateUrlRequest("https://example.com/first", alias);
        var req2 = new CreateUrlRequest("https://example.com/second", alias);

        var resp1 = await _client.PostAsJsonAsync("/api/v1/urls", req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var resp2 = await _client.PostAsJsonAsync("/api/v1/urls", req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);

        // Verify RFC 7807 ProblemDetails structure
        var problem = await resp2.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        Assert.Equal(409, problem?.Status);
        Assert.Equal("Alias Conflict", problem?.Title);
        Assert.Contains(alias, problem?.Detail ?? "");
    }

    [Fact]
    public async Task ConcurrentAliasCollision_OneSucceedsOneGets409()
    {
        // Simulate concurrent alias claims by firing two requests in parallel.
        var alias = "concurrent-race";

        var task1 = _client.PostAsJsonAsync("/api/v1/urls",
            new CreateUrlRequest("https://example.com/a", alias));
        var task2 = _client.PostAsJsonAsync("/api/v1/urls",
            new CreateUrlRequest("https://example.com/b", alias));

        var results = await Task.WhenAll(task1, task2);

        var statuses = results.Select(r => r.StatusCode).OrderBy(s => s).ToList();

        // Exactly one must succeed (201), the other must conflict (409).
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    private record ProblemDetailsResponse(string? Title, string? Detail, int? Status);
}

// ═══════════════════════════════════════════════════════════════════
//  SECTION 4: Rate Limit Exhaustion
//  (Isolated factory — dedicated rate limiter budget)
// ═══════════════════════════════════════════════════════════════════

public class RateLimitTests : IClassFixture<IsolatedWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(IsolatedWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Exhaustion_Returns429WithRetryAfterHeader()
    {
        // The middleware defaults to 30 requests per minute per client IP.
        HttpResponseMessage? rateLimitedResponse = null;

        for (int i = 0; i < 35; i++)
        {
            var resp = await _client.GetAsync($"/rate-limit-probe-{i}");
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = resp;
                break;
            }
        }

        Assert.NotNull(rateLimitedResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse!.StatusCode);
        Assert.True(rateLimitedResponse.Headers.Contains("Retry-After"),
            "429 response must contain Retry-After header.");
    }
}
