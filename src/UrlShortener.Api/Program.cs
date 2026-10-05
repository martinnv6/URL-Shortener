using UrlShortener.Api.Endpoints;
using UrlShortener.Core.Interfaces;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Dependency Injection Configuration
// ---------------------------------------------------------------------------
// InMemoryUrlRepository is registered as Singleton because it holds the
// application state (ConcurrentDictionary + sequence counter).
// UrlShortenerService is Scoped: one instance per HTTP request,
// allowing future per-request concerns (logging context, etc.).
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IUrlRepository, InMemoryUrlRepository>();
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map the URL shortener endpoints.
app.MapUrlEndpoints();

app.Run();
