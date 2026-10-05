using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Endpoints;
using UrlShortener.Core.Interfaces;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Dependency Injection Configuration
// ---------------------------------------------------------------------------

// EF Core — SQLite persistence with connection string from appsettings.json.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repository — Scoped to match DbContext lifetime (one per HTTP request).
builder.Services.AddScoped<IUrlRepository, SqliteUrlRepository>();

// Service — Scoped: one instance per HTTP request, allowing future
// per-request concerns (logging context, etc.).
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

// Analytics — Bounded channel (singleton: shared across all threads).
builder.Services.AddSingleton<ClickEventChannel>();

// Analytics — Service (singleton: stateless, writes to the singleton channel).
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();

// Analytics — Background worker (hosted service: reads from the channel
// and persists to the database in batches).
builder.Services.AddHostedService<AnalyticsProcessingWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database Initialization
// ---------------------------------------------------------------------------
// EnsureCreated() creates the database and schema if they don't exist.
// NOTE: This is NOT safe for schema evolution — use Migrations in production.
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map the URL shortener endpoints.
app.MapUrlEndpoints();

app.Run();

// Expose Program class for functional testing
public partial class Program { }
