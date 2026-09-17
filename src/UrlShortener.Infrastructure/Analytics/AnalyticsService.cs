using Microsoft.Extensions.Logging;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// Non-blocking implementation of <see cref="IAnalyticsService"/>.
/// Writes click events to a bounded <see cref="ClickEventChannel"/> using
/// <see cref="System.Threading.Channels.ChannelWriter{T}.TryWrite"/> —
/// this never blocks, never throws, and never performs I/O.
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    private readonly ClickEventChannel _channel;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(ClickEventChannel channel, ILogger<AnalyticsService> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void TrackClick(string shortCode, string? userAgent, string? referer)
    {
        var clickEvent = new ClickEvent
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            TimestampUtc = DateTime.UtcNow,
            UserAgent = userAgent,
            Referer = referer
        };

        if (!_channel.Writer.TryWrite(clickEvent))
        {
            // Channel is bounded with DropOldest — TryWrite should virtually never
            // return false (the channel drops the oldest item to make room).
            // If it does, the channel was completed/disposed — log and move on.
            _logger.LogWarning(
                "Failed to enqueue click event for short code '{ShortCode}'. Channel may be completed.",
                shortCode);
        }
    }
}
