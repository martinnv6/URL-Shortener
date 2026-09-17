using System.Threading.Channels;
using UrlShortener.Core.Entities;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// Encapsulates a bounded <see cref="Channel{T}"/> for <see cref="ClickEvent"/> analytics.
/// Registered as a singleton in DI — shared between the HTTP threads (writers)
/// and the <see cref="AnalyticsProcessingWorker"/> (single reader).
///
/// <para><b>Capacity:</b> 10,000 items.</para>
/// <para><b>Full mode:</b> <see cref="BoundedChannelFullMode.DropOldest"/> —
/// under extreme load, the oldest unprocessed event is silently discarded
/// to prevent OOM crashes. This is an explicit risk control.</para>
/// </summary>
public sealed class ClickEventChannel
{
    private const int Capacity = 10_000;

    private readonly Channel<ClickEvent> _channel;

    public ClickEventChannel()
    {
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,   // Only the background worker reads.
            SingleWriter = false   // Multiple HTTP threads write concurrently.
        });
    }

    /// <summary>Gets the writer half of the channel.</summary>
    public ChannelWriter<ClickEvent> Writer => _channel.Writer;

    /// <summary>Gets the reader half of the channel.</summary>
    public ChannelReader<ClickEvent> Reader => _channel.Reader;
}
