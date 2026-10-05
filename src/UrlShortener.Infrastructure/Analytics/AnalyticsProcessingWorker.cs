using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// Background worker that continuously reads <see cref="ClickEvent"/> records
/// from the bounded <see cref="ClickEventChannel"/> and persists them to the
/// database in batches.
///
/// <para><b>Batching strategy:</b> Accumulates up to 100 events or waits a
/// maximum of 1 second before flushing — whichever threshold is reached first.
/// This amortizes the cost of database round-trips under high load.</para>
///
/// <para><b>DI scope:</b> Creates a new <see cref="IServiceScope"/> per batch
/// to obtain a scoped <see cref="AppDbContext"/>. Background services are singletons
/// and must not inject scoped services directly.</para>
/// </summary>
public sealed class AnalyticsProcessingWorker : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(1);

    private readonly ClickEventChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsProcessingWorker> _logger;

    public AnalyticsProcessingWorker(
        ClickEventChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<AnalyticsProcessingWorker> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analytics processing worker started.");

        var batch = new List<ClickEvent>(BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                batch.Clear();

                // Wait for the first item — this suspends the loop when the channel
                // is empty, yielding the thread back to the thread pool.
                try
                {
                    await _channel.Reader.WaitToReadAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Drain up to BatchSize items or until BatchTimeout elapses.
                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                batchCts.CancelAfter(BatchTimeout);

                try
                {
                    while (batch.Count < BatchSize &&
                           _channel.Reader.TryRead(out var clickEvent))
                    {
                        batch.Add(clickEvent);
                    }

                    // If we haven't filled the batch yet, wait for more items
                    // until the batch timeout fires.
                    while (batch.Count < BatchSize)
                    {
                        var item = await _channel.Reader.ReadAsync(batchCts.Token);
                        batch.Add(item);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Batch timeout fired or shutdown requested — flush what we have.
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Analytics processing worker encountered an unexpected error.");
            throw;
        }
        finally
        {
            // Graceful shutdown: drain any remaining items from the channel.
            while (_channel.Reader.TryRead(out var remaining))
            {
                batch.Add(remaining);
            }

            if (batch.Count > 0)
            {
                _logger.LogInformation(
                    "Flushing {Count} remaining analytics events during shutdown.", batch.Count);
                await FlushBatchAsync(batch, CancellationToken.None);
            }

            _logger.LogInformation("Analytics processing worker stopped.");
        }
    }

    private async Task FlushBatchAsync(List<ClickEvent> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.ClickEvents.AddRange(batch);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Persisted {Count} click events.", batch.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log but do not crash the worker — analytics loss is preferable to
            // bringing down the background service entirely.
            _logger.LogError(ex, "Failed to persist batch of {Count} click events.", batch.Count);
        }
    }
}
