namespace StateStore.Abstractions;

/// <summary>
/// Defines a middleware component that participates in the state store pipeline.
/// Middleware is executed in registration order and can inspect, transform, or
/// short-circuit read, write, and delete operations.
/// </summary>
public interface IStateStoreMiddleware
{
    /// <summary>
    /// Intercepts a read operation. Call <paramref name="next"/> to continue the pipeline,
    /// or return a value directly to short-circuit.
    /// </summary>
    /// <param name="key">The key being read.</param>
    /// <param name="next">A delegate that invokes the next middleware or the storage provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw bytes read from storage, or <c>null</c> if not found.</returns>
    ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken);

    /// <summary>
    /// Intercepts a write operation. Call <paramref name="next"/> to continue the pipeline,
    /// or skip it to prevent the write from reaching the storage provider.
    /// </summary>
    /// <param name="key">The key being written.</param>
    /// <param name="data">The raw bytes being written.</param>
    /// <param name="next">A delegate that invokes the next middleware or the storage provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken);

    /// <summary>
    /// Intercepts a delete operation. Call <paramref name="next"/> to continue the pipeline,
    /// or skip it to prevent the delete from reaching the storage provider.
    /// </summary>
    /// <param name="key">The key being deleted.</param>
    /// <param name="next">A delegate that invokes the next middleware or the storage provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken);
}
