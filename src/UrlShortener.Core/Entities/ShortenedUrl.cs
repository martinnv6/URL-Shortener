namespace UrlShortener.Core.Entities;

/// <summary>
/// Represents a shortened URL mapping in the domain.
/// This is a pure domain entity with zero infrastructure dependencies.
/// </summary>
public sealed class ShortenedUrl
{
    /// <summary>Primary key. Auto-increment sequence used as Base62 input.</summary>
    public long Id { get; set; }

    /// <summary>The Base62-encoded short code or custom alias (max 15 chars).</summary>
    public required string ShortCode { get; set; }

    /// <summary>The original destination URI (max 2048 chars).</summary>
    public required string OriginalUrl { get; set; }

    /// <summary>UTC timestamp of creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optional TTL-based expiration. Null means the URL never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
