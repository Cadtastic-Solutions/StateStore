namespace StateStore.Abstractions;

/// <summary>
/// Defines an auto-save strategy that determines when dirty state should be flushed to storage.
/// Strategies are composable — multiple strategies can be active simultaneously.
/// </summary>
public interface IAutoSaveStrategy : IDisposable
{
    /// <summary>
    /// Starts the auto-save strategy with the provided flush callback.
    /// </summary>
    /// <param name="flushAsync">
    /// A callback that flushes all dirty state to the storage provider.
    /// The strategy should invoke this when its trigger condition is met.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the strategy.</param>
    Task StartAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the auto-save strategy gracefully.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the stop operation.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
