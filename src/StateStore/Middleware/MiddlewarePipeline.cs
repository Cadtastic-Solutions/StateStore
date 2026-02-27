using StateStore.Abstractions;
using StateStore.Exceptions;

namespace StateStore.Middleware;

/// <summary>
/// Executes a chain of <see cref="IStateStoreMiddleware"/> components in registration order,
/// terminating at the <see cref="IStorageProvider"/>.
/// </summary>
internal sealed class MiddlewarePipeline
{
    private readonly IReadOnlyList<IStateStoreMiddleware> _middlewares;
    private readonly IStorageProvider _provider;

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewarePipeline"/>.
    /// </summary>
    /// <param name="middlewares">The ordered list of middleware components.</param>
    /// <param name="provider">The terminal storage provider.</param>
    public MiddlewarePipeline(IReadOnlyList<IStateStoreMiddleware> middlewares, IStorageProvider provider)
    {
        _middlewares = middlewares;
        _provider = provider;
    }

    /// <summary>
    /// Executes the read pipeline for the specified key.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw bytes, or <c>null</c> if not found.</returns>
    public ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        return BuildReadChain(0, key, cancellationToken);
    }

    /// <summary>
    /// Executes the write pipeline for the specified key.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        return BuildWriteChain(0, key, data, cancellationToken);
    }

    /// <summary>
    /// Executes the delete pipeline for the specified key.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken)
    {
        return BuildDeleteChain(0, key, cancellationToken);
    }

    /// <summary>
    /// Checks existence by delegating directly to the storage provider (no middleware).
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><c>true</c> if the key exists; otherwise, <c>false</c>.</returns>
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        return _provider.ExistsAsync(key, cancellationToken);
    }

    private ValueTask<byte[]?> BuildReadChain(int index, string key, CancellationToken cancellationToken)
    {
        if (index >= _middlewares.Count)
        {
            return _provider.ReadAsync(key, cancellationToken);
        }

        var middleware = _middlewares[index];
        try
        {
            return middleware.OnReadAsync(
                key,
                () => BuildReadChain(index + 1, key, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
        {
            throw new MiddlewareException(
                $"Middleware '{middleware.GetType().Name}' threw an unhandled exception during read for key '{key}'.",
                middleware.GetType(),
                ex);
        }
    }

    private ValueTask BuildWriteChain(int index, string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (index >= _middlewares.Count)
        {
            return _provider.WriteAsync(key, data, cancellationToken);
        }

        var middleware = _middlewares[index];
        try
        {
            return middleware.OnWriteAsync(
                key,
                data,
                () => BuildWriteChain(index + 1, key, data, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
        {
            throw new MiddlewareException(
                $"Middleware '{middleware.GetType().Name}' threw an unhandled exception during write for key '{key}'.",
                middleware.GetType(),
                ex);
        }
    }

    private ValueTask BuildDeleteChain(int index, string key, CancellationToken cancellationToken)
    {
        if (index >= _middlewares.Count)
        {
            return _provider.DeleteAsync(key, cancellationToken);
        }

        var middleware = _middlewares[index];
        try
        {
            return middleware.OnDeleteAsync(
                key,
                () => BuildDeleteChain(index + 1, key, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
        {
            throw new MiddlewareException(
                $"Middleware '{middleware.GetType().Name}' threw an unhandled exception during delete for key '{key}'.",
                middleware.GetType(),
                ex);
        }
    }
}
