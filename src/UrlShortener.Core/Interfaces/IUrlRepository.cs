using UrlShortener.Core.Entities;

namespace UrlShortener.Core.Interfaces;

/// <summary>
/// Repository abstraction for URL persistence.
/// Defined in Core to satisfy the Dependency Inversion Principle:
/// Core defines the contract; Infrastructure provides the implementation.
/// </summary>
public interface IUrlRepository
{
    /// <summary>
    /// Retrieves a shortened URL by its short code.
    /// Returns null if the code does not exist.
    /// </summary>
    Task<ShortenedUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new shortened URL. Generates a Base62 short code from the
    /// internal sequence counter, or uses the provided custom alias.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the short code or alias already exists.</exception>
    Task<ShortenedUrl> CreateAsync(string originalUrl, string? customAlias = null, CancellationToken cancellationToken = default);
}
