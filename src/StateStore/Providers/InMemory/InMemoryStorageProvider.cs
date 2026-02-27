using System.Collections.Concurrent;
using StateStore.Abstractions;

namespace StateStore.Providers.InMemory;

/// <summary>
/// An in-memory storage provider backed by <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// Thread-safe for individual operations. Suitable for testing and ephemeral scenarios.
/// <para>
/// <b>Note:</b> Compound operations (such as read-modify-write) are not atomic and must be synchronized by the caller or handled at a higher abstraction.
/// </para>
/// </summary>
public sealed class InMemoryStorageProvider : IStorageProvider
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    /// <inheritdoc />
    public ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetValue(key, out var data);
        return new ValueTask<byte[]?>(data);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store[key] = data.ToArray();
        return default;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryRemove(key, out _);
        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<bool>(_store.ContainsKey(key));
    }

    /// <summary>
    /// Returns all keys currently stored. Useful for test assertions.
    /// </summary>
    /// <returns>A collection of all stored keys.</returns>
    public ICollection<string> GetAllKeys() => _store.Keys;

    /// <summary>
    /// Removes all entries from the store. Useful for test isolation.
    /// </summary>
    public void Clear() => _store.Clear();
}
