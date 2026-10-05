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
///
/// OWASP API7:2023 — Concurrency-safe alias management:
/// The unique index on ShortCode is the authoritative enforcement mechanism.
/// AnyAsync is retained as a fast-path optimization, but a DbUpdateException
/// catch around SaveChangesAsync handles the TOCTOU race condition.
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
            // Fast-path pre-check (non-authoritative — subject to TOCTOU race).
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

        try
        {
            // Phase 1: Insert to obtain the auto-incremented Id.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Authoritative catch: unique index violation under concurrent writes.
            throw new InvalidOperationException(
                $"Short code '{customAlias}' already exists. Custom aliases must be unique.", ex);
        }

        if (customAlias is null)
        {
            // Phase 2: Derive the short code from the database-generated Id.
            entity.ShortCode = Base62Encoder.Encode((ulong)entity.Id);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return entity;
    }

    /// <summary>
    /// Detects whether a DbUpdateException was caused by a UNIQUE constraint violation.
    /// SQLite error codes: SQLITE_CONSTRAINT (19), SQLITE_CONSTRAINT_UNIQUE (2067).
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // Microsoft.Data.Sqlite wraps errors as SqliteException.
        // We check the message to avoid a hard dependency on the Sqlite package from this layer.
        return ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqliteEx
            && (sqliteEx.SqliteErrorCode == 19 || sqliteEx.SqliteExtendedErrorCode == 2067);
    }
}
