namespace UrlShortener.Core.Interfaces;

/// <summary>
/// Non-blocking analytics service abstraction.
/// <see cref="TrackClick"/> is intentionally synchronous (void) because it
/// performs only a <c>TryWrite</c> to a bounded in-memory channel — no I/O.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Records a click event for the given short code.
    /// This method is non-blocking and fire-and-forget; it never throws
    /// even if the internal buffer is full (oldest events are dropped).
    /// </summary>
    /// <param name="shortCode">The short code that was accessed.</param>
    /// <param name="userAgent">The client's User-Agent header, if available.</param>
    /// <param name="referer">The client's Referer header, if available.</param>
    void TrackClick(string shortCode, string? userAgent, string? referer);
}
