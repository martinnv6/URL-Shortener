using System.Collections.Concurrent;
using UrlShortener.Core.Encoding;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IUrlRepository"/>.
///
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads
/// and an atomic sequence counter via <see cref="Interlocked.Increment(ref long)"/>
/// to guarantee unique, monotonically increasing IDs.
///
/// Design note: <see cref="Interlocked.Increment(ref long)"/> is used because
/// .NET 8 does not expose an unsigned 64-bit overload. The <c>long</c> counter
/// is cast to <c>ulong</c> for Base62 encoding. With a positive-only sequence,
/// this is safe up to <see cref="long.MaxValue"/> (9.2 quintillion IDs).
/// </summary>
public sealed class InMemoryUrlRepository : IUrlRepository
{
    private readonly ConcurrentDictionary<string, ShortenedUrl> _store = new(StringComparer.Ordinal);

    /// <summary>
    /// Atomic sequence counter. Incremented via <see cref="Interlocked.Increment(ref long)"/>
    /// to guarantee thread-safe, lock-free ID generation across concurrent requests.
    /// </summary>
    private long _sequenceCounter;

    /// <inheritdoc />
    public Task<ShortenedUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(shortCode, out var url);
        return Task.FromResult(url);
    }

    /// <inheritdoc />
    public Task<ShortenedUrl> CreateAsync(string originalUrl, string? customAlias = null, CancellationToken cancellationToken = default)
    {
        // Atomically generate the next unique ID.
        var id = Interlocked.Increment(ref _sequenceCounter);
        var shortCode = customAlias ?? Base62Encoder.Encode((ulong)id);

        var entity = new ShortenedUrl
        {
            Id = id,
            ShortCode = shortCode,
            OriginalUrl = originalUrl,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // TryAdd is atomic — if the alias already exists, we fail fast.
        // This prevents TOCTOU race conditions where a "check-then-insert"
        // pattern could allow two concurrent requests to claim the same alias.
        if (!_store.TryAdd(shortCode, entity))
        {
            throw new InvalidOperationException(
                $"Short code '{shortCode}' already exists. Custom aliases must be unique.");
        }

        return Task.FromResult(entity);
    }
}
