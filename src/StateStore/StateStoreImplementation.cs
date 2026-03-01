using Microsoft.Extensions.Logging;
using StateStore.Abstractions;
using StateStore.Concurrency;
using StateStore.Exceptions;
using StateStore.Internal;
using StateStore.Middleware;

namespace StateStore;

/// <summary>
/// Core implementation of <see cref="IStateStore"/> that coordinates serialization,
/// middleware pipeline, concurrency control, and storage provider access.
/// </summary>
/// <summary>
/// Core implementation of <see cref="IStateStore"/> that coordinates serialization,
/// middleware pipeline, concurrency control, and storage provider access.
/// </summary>
/// <remarks>
/// <b>Thread Safety:</b> All public methods are thread-safe. Compound operations (such as upsert) are atomic per key.
/// </remarks>
internal sealed class StateStoreImplementation : IStateStore
{
    private readonly IStateSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly KeyedAsyncLock _locks = new();
    private readonly IDirtyKeyTracker? _dirtyTracker;
    private readonly ILogger<StateStoreImplementation>? _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreImplementation"/>.
    /// </summary>
    /// <param name="serializer">The serializer for converting state to/from bytes.</param>
    /// <param name="pipeline">The middleware pipeline terminating at the storage provider.</param>
    /// <param name="dirtyTracker">Optional dirty key tracker for auto-save support.</param>
    /// <param name="logger">Optional logger for structured logging.</param>
    public StateStoreImplementation(
        IStateSerializer serializer,
        MiddlewarePipeline pipeline,
        IDirtyKeyTracker? dirtyTracker = null,
        ILogger<StateStoreImplementation>? logger = null)
    {
        _serializer = serializer;
        _pipeline = pipeline;
        _dirtyTracker = dirtyTracker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        KeyHelper.ValidateKey(key);

        _logger?.LogDebug("Getting state for key '{Key}' of type {Type}", key, typeof(T).FullName);

        byte[]? data;
        using (await _locks.AcquireReadLockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                data = await _pipeline.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to read state for key '{Key}'", key);
                throw new StorageProviderException($"Failed to read state for key '{key}'.", key, "Read", _pipeline.GetType(), ex);
            }
        }

        if (data is null)
        {
            _logger?.LogDebug("No data found for key '{Key}'", key);
            return default;
        }

        var storedState = _serializer.Deserialize<StoredState<T>>(data);
        _logger?.LogDebug("Deserialized state for key '{Key}'", key);
        return storedState is not null ? storedState.Value : default;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        KeyHelper.ValidateKey(key);

        _logger?.LogDebug("Setting state for key '{Key}' of type {Type}", key, typeof(T).FullName);

        using (await _locks.AcquireWriteLockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            var storedState = await ReadStoredStateOrDefaultAsync<T>(key, cancellationToken).ConfigureAwait(false);
            var newState = storedState is not null
                ? StoredState<T>.CreateUpdated(value, storedState)
                : StoredState<T>.CreateNew(value);

            var bytes = _serializer.Serialize(newState);

            try
            {
                await _pipeline.WriteAsync(key, bytes, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("State written for key '{Key}'", key);
            }
            catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to write state for key '{Key}'", key);
                throw new StorageProviderException(
                    $"Failed to write state for key '{key}'.",
                    key, "Write", _pipeline.GetType(), ex);
            }
        }

        _dirtyTracker?.MarkDirty(key);
    }

    /// <inheritdoc />
    public async ValueTask UpsertAsync<T>(string key, T insertValue, Func<T, T> updateFactory, CancellationToken cancellationToken = default)
    {
        KeyHelper.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(updateFactory);

        _logger?.LogDebug("Upserting state for key '{Key}' of type {Type}", key, typeof(T).FullName);

        using (await _locks.AcquireWriteLockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            var existingState = await ReadStoredStateOrDefaultAsync<T>(key, cancellationToken).ConfigureAwait(false);

            StoredState<T> newState;
            if (existingState is not null && existingState.Value is not null)
            {
                var updatedValue = updateFactory(existingState.Value!);
                newState = StoredState<T>.CreateUpdated(updatedValue, existingState);
            }
            else
            {
                newState = StoredState<T>.CreateNew(insertValue);
            }

            var bytes = _serializer.Serialize(newState);

            try
            {
                await _pipeline.WriteAsync(key, bytes, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("State upserted for key '{Key}'", key);
            }
            catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to upsert state for key '{Key}'", key);
                throw new StorageProviderException(
                    $"Failed to write state for key '{key}' during upsert.",
                    key, "Write", _pipeline.GetType(), ex);
            }
        }

        _dirtyTracker?.MarkDirty(key);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        KeyHelper.ValidateKey(key);

        _logger?.LogDebug("Deleting state for key '{Key}'", key);

        using (await _locks.AcquireWriteLockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await _pipeline.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("State deleted for key '{Key}'", key);
            }
            catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to delete state for key '{Key}'", key);
                throw new StorageProviderException(
                    $"Failed to delete state for key '{key}'.",
                    key, "Delete", _pipeline.GetType(), ex);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        KeyHelper.ValidateKey(key);

        _logger?.LogDebug("Checking existence for key '{Key}'", key);

        using (await _locks.AcquireReadLockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var exists = await _pipeline.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
                _logger?.LogDebug("Existence for key '{Key}': {Exists}", key, exists);
                return exists;
            }
            catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to check existence for key '{Key}'", key);
                throw new StorageProviderException(
                    $"Failed to check existence for key '{key}'.",
                    key, "Exists", _pipeline.GetType(), ex);
            }
        }
    }

    private async ValueTask<StoredState<T>?> ReadStoredStateOrDefaultAsync<T>(string key, CancellationToken cancellationToken)
    {
        byte[]? data;
        try
        {
            data = await _pipeline.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
        {
            throw new StorageProviderException(
                $"Failed to read state for key '{key}'.",
                key, "Read", _pipeline.GetType(), ex);
        }

        return data is null ? null : _serializer.Deserialize<StoredState<T>>(data);
    }
}
