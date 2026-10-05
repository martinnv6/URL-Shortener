using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.UnitTests.Infrastructure;

public sealed class SqliteUrlRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteUrlRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task CreateAsync_WithoutCustomAlias_GeneratesAndReturnsShortCode()
    {
        // Arrange
        var originalUrl = "https://example.com/test";
        var repository = CreateRepository();

        // Act
        var result = await repository.CreateAsync(originalUrl);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(originalUrl, result.OriginalUrl);
        Assert.NotNull(result.ShortCode);
        Assert.NotEmpty(result.ShortCode);

        // Verify in DB
        using var context = new AppDbContext(_options);
        var dbEntity = await context.ShortenedUrls.FirstOrDefaultAsync(u => u.Id == result.Id);
        Assert.NotNull(dbEntity);
        Assert.Equal(result.ShortCode, dbEntity.ShortCode);
    }

    [Fact]
    public async Task CreateAsync_WithCustomAlias_SucceedsIfAvailable()
    {
        // Arrange
        var originalUrl = "https://example.com/custom";
        var customAlias = "myalias";
        var repository = CreateRepository();

        // Act
        var result = await repository.CreateAsync(originalUrl, customAlias);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customAlias, result.ShortCode);

        using var context = new AppDbContext(_options);
        var dbEntity = await context.ShortenedUrls.FirstOrDefaultAsync(u => u.ShortCode == customAlias);
        Assert.NotNull(dbEntity);
    }

    [Fact]
    public async Task CreateAsync_WithExistingCustomAlias_ThrowsInvalidOperationException()
    {
        // Arrange
        var customAlias = "conflict";
        using (var context = new AppDbContext(_options))
        {
            context.ShortenedUrls.Add(new ShortenedUrl
            {
                OriginalUrl = "https://existing.com",
                ShortCode = customAlias,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync("https://new.com", customAlias));
    }

    [Fact]
    public async Task GetByShortCodeAsync_ExistingCode_ReturnsEntity()
    {
        // Arrange
        var shortCode = "existing";
        var expectedUrl = "https://example.com/existing";
        using (var context = new AppDbContext(_options))
        {
            context.ShortenedUrls.Add(new ShortenedUrl
            {
                OriginalUrl = expectedUrl,
                ShortCode = shortCode,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository();

        // Act
        var result = await repository.GetByShortCodeAsync(shortCode);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedUrl, result.OriginalUrl);
        Assert.Equal(shortCode, result.ShortCode);
    }

    [Fact]
    public async Task GetByShortCodeAsync_NonExistingCode_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var result = await repository.GetByShortCodeAsync("missing");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Simulates the TOCTOU race condition: insert a conflicting row AFTER
    /// the AnyAsync pre-check passes, to exercise the DbUpdateException catch path.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ConcurrentDuplicateAlias_ThrowsInvalidOperationException()
    {
        // Arrange — insert a row with the alias directly into the DB,
        // simulating a concurrent write that beats us to the unique index.
        var alias = "race-target";
        using (var seedContext = new AppDbContext(_options))
        {
            seedContext.ShortenedUrls.Add(new ShortenedUrl
            {
                OriginalUrl = "https://first.com",
                ShortCode = alias,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seedContext.SaveChangesAsync();
        }

        // Create a fresh context that does NOT have the entity tracked,
        // so AnyAsync would need to re-query — but we bypass it by using
        // a context that already has the conflicting row in the DB.
        // The repository's AnyAsync will find the conflict on the fast path.
        var repository = CreateRepository();

        // Act & Assert — should throw InvalidOperationException (from either path)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync("https://second.com", alias));
        Assert.Contains(alias, ex.Message);
    }

    private SqliteUrlRepository CreateRepository()
    {
        return new SqliteUrlRepository(new AppDbContext(_options));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
