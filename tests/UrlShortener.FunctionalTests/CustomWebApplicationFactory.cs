using System.Data.Common;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Contracts;
using UrlShortener.Infrastructure.Persistence;
using Xunit;

namespace UrlShortener.FunctionalTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = default!;
    private readonly string _dbFileName;
    private readonly string _connectionString;

    public CustomWebApplicationFactory()
    {
        _dbFileName = $"urlshortener.functionaltests.{Guid.NewGuid()}.db";
        _connectionString = $"Data Source={_dbFileName}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            var dbConnectionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbConnection));
            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            // Create open connection to SQLite
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            services.AddDbContext<AppDbContext>((container, options) =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    public async Task InitializeAsync()
    {
        if (File.Exists(_dbFileName))
        {
            File.Delete(_dbFileName);
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public new async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }

        if (File.Exists(_dbFileName))
        {
            File.Delete(_dbFileName);
        }
        if (File.Exists(_dbFileName + "-shm")) File.Delete(_dbFileName + "-shm");
        if (File.Exists(_dbFileName + "-wal")) File.Delete(_dbFileName + "-wal");
        
        await base.DisposeAsync();
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task WaitForAnalyticsAsync(string shortCode, Func<ClickAnalyticsResponse, bool> condition, TimeSpan timeout)
    {
        using var client = CreateClient();
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var response = await client.GetAsync($"/api/v1/urls/{shortCode}/analytics");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();
                if (result != null && condition(result))
                {
                    return; // Condition met!
                }
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was not met within {timeout.TotalSeconds} seconds.");
    }
}
