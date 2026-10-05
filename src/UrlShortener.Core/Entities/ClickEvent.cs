namespace UrlShortener.Core.Entities;

/// <summary>
/// Represents a single click/redirect analytics event.
/// Published to a bounded <see cref="System.Threading.Channels.Channel{T}"/>
/// on each redirect and persisted asynchronously by a background worker.
/// </summary>
public sealed class ClickEvent
{
    /// <summary>Unique event identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The short code that was accessed (max 15 chars).</summary>
    public required string ShortCode { get; set; }

    /// <summary>UTC timestamp of the click event.</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Browser/client user agent string.</summary>
    public string? UserAgent { get; set; }

    /// <summary>The referring page URL, if available.</summary>
    public string? Referer { get; set; }
}
