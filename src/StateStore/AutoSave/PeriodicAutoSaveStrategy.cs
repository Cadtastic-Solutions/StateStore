using StateStore.Abstractions;

namespace StateStore.AutoSave;

/// <summary>
/// An auto-save strategy that flushes dirty state on a configurable periodic interval.
/// </summary>
public sealed class PeriodicAutoSaveStrategy : IAutoSaveStrategy
{
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="PeriodicAutoSaveStrategy"/>.
    /// </summary>
    /// <param name="interval">
    /// The interval between flush cycles. Must be at least 1 second.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is less than 1 second.</exception>
    public PeriodicAutoSaveStrategy(TimeSpan interval)
    {
        if (interval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Periodic auto-save interval must be at least 1 second.");
        }

        _interval = interval;
    }

    /// <inheritdoc />
    public Task StartAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningTask = RunPeriodicFlushAsync(flushAsync, _cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
#if NET9_0_OR_GREATER
            await _cts.CancelAsync().ConfigureAwait(false);
#else
            _cts.Cancel();
#endif
        }

        if (_runningTask is not null)
        {
            try
            {
                await _runningTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Dispose();
    }

    private async Task RunPeriodicFlushAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await flushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
