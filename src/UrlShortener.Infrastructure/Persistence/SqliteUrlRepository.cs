using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Encoding;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>
/// EF Core + SQLite implementation of <see cref="IUrlRepository"/>.
///
/// Short code generation uses a two-phase approach:
///   1. Insert the entity to obtain the auto-incremented <see cref="ShortenedUrl.Id"/>.
///   2. Derive the <see cref="ShortenedUrl.ShortCode"/> via <see cref="Base62Encoder.Encode"/>
///      and update the entity.
///
/// This preserves the deterministic Base62 encoding from the in-memory implementation
/// while leveraging the database's identity column for thread-safe ID generation.
/// </summary>
public sealed class SqliteUrlRepository : IUrlRepository
{
    private readonly AppDbContext _context;

    public SqliteUrlRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<ShortenedUrl?> GetByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        return await _context.ShortenedUrls
            .FirstOrDefaultAsync(u => u.ShortCode == shortCode, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ShortenedUrl> CreateAsync(
        string originalUrl,
        string? customAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (customAlias is not null)
        {
            // Check for alias conflicts before inserting.
            var exists = await _context.ShortenedUrls
                .AnyAsync(u => u.ShortCode == customAlias, cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException(
                    $"Short code '{customAlias}' already exists. Custom aliases must be unique.");
            }
        }

        var entity = new ShortenedUrl
        {
            // ShortCode is set to a temporary placeholder for custom aliases,
            // or will be derived from the auto-incremented Id.
            ShortCode = customAlias ?? string.Empty,
            OriginalUrl = originalUrl,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ShortenedUrls.Add(entity);

        // Phase 1: Insert to obtain the auto-incremented Id.
        await _context.SaveChangesAsync(cancellationToken);

        if (customAlias is null)
        {
            // Phase 2: Derive the short code from the database-generated Id.
            entity.ShortCode = Base62Encoder.Encode((ulong)entity.Id);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return entity;
    }
}
