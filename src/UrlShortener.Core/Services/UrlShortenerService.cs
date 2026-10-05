using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Core.Services;

/// <summary>
/// Default implementation of <see cref="IUrlShortenerService"/>.
/// Delegates persistence to <see cref="IUrlRepository"/> (Dependency Inversion).
/// Currently a thin pass-through; will grow with caching, analytics dispatch,
/// and custom alias validation in subsequent iterations.
/// </summary>
public sealed class UrlShortenerService : IUrlShortenerService
{
    private readonly IUrlRepository _repository;

    public UrlShortenerService(IUrlRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<ShortenedUrl> CreateAsync(
        string originalUrl,
        string? customAlias = null,
        CancellationToken cancellationToken = default)
    {
        return await _repository.CreateAsync(originalUrl, customAlias, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ShortenedUrl?> GetByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        return await _repository.GetByShortCodeAsync(shortCode, cancellationToken);
    }
}
