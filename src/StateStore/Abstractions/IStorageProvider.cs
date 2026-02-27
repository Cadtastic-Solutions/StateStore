namespace StateStore.Abstractions;

/// <summary>
/// Abstracts raw read/write/delete operations for a storage backend.
/// Implementations operate exclusively on byte arrays and have no knowledge
/// of serialization or application types.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Reads the raw bytes for the specified key.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored bytes, or <c>null</c> if the key does not exist.</returns>
    ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes raw bytes for the specified key, creating or overwriting the entry.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="data">The raw bytes to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry for the specified key. This is a no-op if the key does not exist.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether an entry exists for the specified key.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><c>true</c> if the key exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
