using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the URL Shortener application.
/// Configures entity mappings via Fluent API to keep Core entities
/// free of infrastructure attributes (Clean Architecture).
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── ShortenedUrl ──────────────────────────────────────────────
        modelBuilder.Entity<ShortenedUrl>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Auto-increment sequence — Base62 input.
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.ShortCode)
                .IsRequired()
                .HasMaxLength(15);

            entity.HasIndex(e => e.ShortCode)
                .IsUnique();

            entity.Property(e => e.OriginalUrl)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });

        // ── ClickEvent ────────────────────────────────────────────────
        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ShortCode)
                .IsRequired()
                .HasMaxLength(15);

            // Non-unique index for analytics queries filtered by ShortCode.
            entity.HasIndex(e => e.ShortCode);

            entity.Property(e => e.TimestampUtc)
                .IsRequired();

            entity.Property(e => e.UserAgent)
                .HasMaxLength(512);

            entity.Property(e => e.Referer)
                .HasMaxLength(2048);
        });
    }
}
