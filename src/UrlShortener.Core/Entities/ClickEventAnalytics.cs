namespace UrlShortener.Core.Entities;

/// <summary>
/// Represents a single click/redirect analytics event.
/// Captured asynchronously via System.Threading.Channels to avoid blocking the read path.
/// Full implementation deferred to the analytics pipeline step.
/// </summary>
public sealed class ClickEventAnalytics
{
    /// <summary>Unique event identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The short code that was accessed.</summary>
    public required string ShortCode { get; set; }

    /// <summary>UTC timestamp of the click event.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Hashed/anonymized IP address for geographic analytics.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Browser/client user agent string.</summary>
    public string? UserAgent { get; set; }

    /// <summary>The referring page URL, if available.</summary>
    public string? Referer { get; set; }
}
