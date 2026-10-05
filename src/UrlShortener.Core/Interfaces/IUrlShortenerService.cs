using UrlShortener.Core.Entities;

namespace UrlShortener.Core.Interfaces;

/// <summary>
/// Application-level service abstraction for URL shortening operations.
/// Sits between the API layer and the repository, providing a coordination
/// point for domain logic, validation, and future cross-cutting concerns
/// (caching, analytics dispatch, etc.).
/// </summary>
public interface IUrlShortenerService
{
    /// <summary>Creates a new shortened URL.</summary>
    Task<ShortenedUrl> CreateAsync(string originalUrl, string? customAlias = null, CancellationToken cancellationToken = default);

    /// <summary>Resolves a short code to its original URL entity.</summary>
    Task<ShortenedUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
}
