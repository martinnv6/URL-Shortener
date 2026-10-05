using Microsoft.AspNetCore.Http;
using UrlShortener.Api.Middleware;

namespace UrlShortener.UnitTests.Middleware;

public sealed class SlidingWindowRateLimiterTests : IDisposable
{
    private readonly SlidingWindowRateLimiterMiddleware _middleware;
    private bool _nextCalled;

    public SlidingWindowRateLimiterTests()
    {
        _nextCalled = false;
        _middleware = new SlidingWindowRateLimiterMiddleware(
            next: _ => { _nextCalled = true; return Task.CompletedTask; },
            maxRequests: 5,     // Small window for fast testing
            windowSeconds: 2);
    }

    [Fact]
    public async Task InvokeAsync_UnderLimit_PassesThrough()
    {
        var context = CreateHttpContext("192.168.1.100");

        await _middleware.InvokeAsync(context);

        Assert.True(_nextCalled);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AtLimit_Returns429()
    {
        var ip = "10.0.0.1";

        // Send 5 requests (at limit)
        for (int i = 0; i < 5; i++)
        {
            var ctx = CreateHttpContext(ip);
            await _middleware.InvokeAsync(ctx);
        }

        // 6th request should be rate limited
        var limitedContext = CreateHttpContext(ip);
        await _middleware.InvokeAsync(limitedContext);

        Assert.Equal(StatusCodes.Status429TooManyRequests, limitedContext.Response.StatusCode);
        Assert.True(limitedContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task InvokeAsync_DifferentIps_IndependentlyTracked()
    {
        // Fill up IP A
        for (int i = 0; i < 5; i++)
        {
            await _middleware.InvokeAsync(CreateHttpContext("1.1.1.1"));
        }

        // IP B should still pass
        var contextB = CreateHttpContext("2.2.2.2");
        await _middleware.InvokeAsync(contextB);

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, contextB.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AfterWindowSlides_AllowsAgain()
    {
        var ip = "3.3.3.3";

        // Fill up the window
        for (int i = 0; i < 5; i++)
        {
            await _middleware.InvokeAsync(CreateHttpContext(ip));
        }

        // Confirm limited
        var limited = CreateHttpContext(ip);
        await _middleware.InvokeAsync(limited);
        Assert.Equal(StatusCodes.Status429TooManyRequests, limited.Response.StatusCode);

        // Wait for window to slide (2 seconds + buffer)
        await Task.Delay(2500);

        // Should be allowed again
        _nextCalled = false;
        var recovered = CreateHttpContext(ip);
        await _middleware.InvokeAsync(recovered);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, recovered.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RetryAfterHeader_IsPositiveInteger()
    {
        var ip = "4.4.4.4";

        for (int i = 0; i < 5; i++)
        {
            await _middleware.InvokeAsync(CreateHttpContext(ip));
        }

        var limited = CreateHttpContext(ip);
        await _middleware.InvokeAsync(limited);

        var retryAfter = limited.Response.Headers["Retry-After"].ToString();
        Assert.True(int.TryParse(retryAfter, out var seconds));
        Assert.True(seconds >= 1);
    }

    private static HttpContext CreateHttpContext(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        context.Response.Body = new MemoryStream();
        return context;
    }

    public void Dispose()
    {
        _middleware.Dispose();
    }
}
