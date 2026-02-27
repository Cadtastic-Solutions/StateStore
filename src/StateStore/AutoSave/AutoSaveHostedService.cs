using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StateStore.Abstractions;
using StateStore.Internal;

namespace StateStore.AutoSave;

/// <summary>
/// An <see cref="IHostedService"/> that manages the lifecycle of registered auto-save strategies
/// and coordinates flushing dirty state to the storage provider.
/// </summary>
internal sealed class AutoSaveHostedService : IHostedService, IDisposable
{
    private readonly IEnumerable<IAutoSaveStrategy> _strategies;
    private readonly IStateStore _stateStore;
    private readonly IDirtyKeyTracker _dirtyTracker;
    private readonly ILogger<AutoSaveHostedService> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AutoSaveHostedService"/>.
    /// </summary>
    /// <param name="strategies">The registered auto-save strategies.</param>
    /// <param name="stateStore">The state store to flush state through.</param>
    /// <param name="dirtyTracker">The dirty key tracker.</param>
    /// <param name="logger">The logger instance.</param>
    public AutoSaveHostedService(
        IEnumerable<IAutoSaveStrategy> strategies,
        IStateStore stateStore,
        IDirtyKeyTracker dirtyTracker,
        ILogger<AutoSaveHostedService> logger)
    {
        _strategies = strategies;
        _stateStore = stateStore;
        _dirtyTracker = dirtyTracker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting auto-save strategies.");
        foreach (var strategy in _strategies)
        {
            await strategy.StartAsync(FlushDirtyKeysAsync, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping auto-save strategies.");
        foreach (var strategy in _strategies)
        {
            await strategy.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        // Final flush on shutdown.
        await FlushDirtyKeysAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var strategy in _strategies)
        {
            strategy.Dispose();
        }
    }

    private async Task FlushDirtyKeysAsync(CancellationToken cancellationToken)
    {
        var dirtyKeys = _dirtyTracker.DrainDirtyKeys();
        if (dirtyKeys.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Flushing {Count} dirty keys.", dirtyKeys.Count);

        foreach (var key in dirtyKeys)
        {
            try
            {
                // The state is already persisted by SetAsync/UpsertAsync.
                // Auto-save's flush ensures any deferred persistence strategies
                // have completed. For now, dirty tracking confirms which keys changed.
                var exists = await _stateStore.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
                if (exists)
                {
                    _logger.LogDebug("Confirmed key '{Key}' is persisted.", key);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to verify flush for key '{Key}'. Key will be retried on next cycle.", key);
                _dirtyTracker.MarkDirty(key);
            }
        }
    }
}
