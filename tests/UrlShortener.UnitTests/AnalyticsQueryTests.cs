using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.UnitTests;

/// <summary>
/// Verifies EF Core SQLite query translation and ordering for ClickEvent analytics.
/// Specifically guards against the SQLite 'DateTimeOffset in ORDER BY' limitation.
/// </summary>
public sealed class AnalyticsQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AnalyticsQueryTests()
    {
        // Open a shared in-memory SQLite connection for the test lifetime.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AnalyticsQuery_OrderByDescendingTimestampUtc_TranslatesAndOrdersCorrectlyInSqlite()
    {
        // Arrange
        const string shortCode = "testCode";
        var baseTime = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

        await using (var context = new AppDbContext(_options))
        {
            context.ClickEvents.AddRange(
                new ClickEvent
                {
                    Id = Guid.NewGuid(),
                    ShortCode = shortCode,
                    TimestampUtc = baseTime.AddMinutes(1),
                    UserAgent = "UA-1"
                },
                new ClickEvent
                {
                    Id = Guid.NewGuid(),
                    ShortCode = shortCode,
                    TimestampUtc = baseTime.AddMinutes(5),
                    UserAgent = "UA-3"
                },
                new ClickEvent
                {
                    Id = Guid.NewGuid(),
                    ShortCode = shortCode,
                    TimestampUtc = baseTime.AddMinutes(3),
                    UserAgent = "UA-2"
                },
                new ClickEvent
                {
                    Id = Guid.NewGuid(),
                    ShortCode = "otherCode",
                    TimestampUtc = baseTime.AddMinutes(10),
                    UserAgent = "UA-Other"
                }
            );

            await context.SaveChangesAsync();
        }

        // Act
        List<ClickEvent> results;
        await using (var context = new AppDbContext(_options))
        {
            results = await context.ClickEvents
                .Where(e => e.ShortCode == shortCode)
                .OrderByDescending(e => e.TimestampUtc)
                .Take(10)
                .ToListAsync();
        }

        // Assert: Successfully translated in SQLite without NotSupportedException
        Assert.Equal(3, results.Count);
        Assert.Equal("UA-3", results[0].UserAgent); // Latest: +5 min
        Assert.Equal("UA-2", results[1].UserAgent); // Middle: +3 min
        Assert.Equal("UA-1", results[2].UserAgent); // Oldest: +1 min
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
