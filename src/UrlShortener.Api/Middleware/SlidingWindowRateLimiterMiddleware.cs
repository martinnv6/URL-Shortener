using System.Collections.Concurrent;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Sliding-window rate limiter middleware.
/// Limits requests to a configurable maximum per time window, keyed by client IP.
///
/// OWASP: Mitigates brute-force attacks, credential stuffing, and API abuse.
///
/// Implementation: In-memory, zero external dependencies. Uses a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of <see cref="ConcurrentQueue{T}"/>
/// for lock-free hot-path performance.
///
/// KNOWN LIMITATION: Per-process, non-durable. Does not survive app restarts.
/// For horizontal scaling, replace with Redis-backed rate limiting.
/// </summary>
public sealed class SlidingWindowRateLimiterMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _clients = new();
    private readonly Timer _cleanupTimer;

    public SlidingWindowRateLimiterMiddleware(
        RequestDelegate next,
        int maxRequests = 30,
        int windowSeconds = 60)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);

        // Periodic cleanup of stale IPs every 5 minutes to prevent unbounded memory growth.
        _cleanupTimer = new Timer(CleanupStaleEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var queue = _clients.GetOrAdd(clientIp, _ => new ConcurrentQueue<DateTime>());

        var now = DateTime.UtcNow;
        var windowStart = now - _window;

        // Drain expired entries from the front of the queue.
        while (queue.TryPeek(out var oldest) && oldest < windowStart)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= _maxRequests)
        {
            // Calculate Retry-After: seconds until the oldest entry expires.
            if (queue.TryPeek(out var nextExpiry))
            {
                var retryAfter = (int)Math.Ceiling((nextExpiry + _window - now).TotalSeconds);
                retryAfter = Math.Max(retryAfter, 1); // At least 1 second.
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
            }

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":"Rate limit exceeded. Please retry after the duration specified in the Retry-After header."}""");
            return;
        }

        queue.Enqueue(now);
        await _next(context);
    }

    private void CleanupStaleEntries(object? state)
    {
        var cutoff = DateTime.UtcNow - _window - TimeSpan.FromMinutes(1);

        foreach (var kvp in _clients)
        {
            // Drain expired entries.
            while (kvp.Value.TryPeek(out var oldest) && oldest < cutoff)
            {
                kvp.Value.TryDequeue(out _);
            }

            // Remove entirely empty entries.
            if (kvp.Value.IsEmpty)
            {
                _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
