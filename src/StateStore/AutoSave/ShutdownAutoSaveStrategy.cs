using Microsoft.Extensions.Hosting;
using StateStore.Abstractions;

namespace StateStore.AutoSave;

/// <summary>
/// An auto-save strategy that flushes dirty state when the host application is stopping.
/// </summary>
public sealed class ShutdownAutoSaveStrategy : IAutoSaveStrategy
{
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenRegistration _registration;
    private Func<CancellationToken, Task>? _flushAsync;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="ShutdownAutoSaveStrategy"/>.
    /// </summary>
    /// <param name="lifetime">The host application lifetime to listen for shutdown events.</param>
    public ShutdownAutoSaveStrategy(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task StartAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken)
    {
        _flushAsync = flushAsync;
        _registration = _lifetime.ApplicationStopping.Register(() =>
        {
            _flushAsync(CancellationToken.None).GetAwaiter().GetResult();
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
#if NET9_0_OR_GREATER
        return _registration.DisposeAsync().AsTask();
#else
        _registration.Dispose();
        return Task.CompletedTask;
#endif
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registration.Dispose();
    }
}
