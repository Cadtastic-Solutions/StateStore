namespace StateStore.Abstractions;

/// <summary>
/// Provides ad-hoc, dictionary-style access to persisted state using string keys.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Retrieves the state associated with the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the stored state.</typeparam>
    /// <param name="key">The unique key identifying the state entry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized state, or <c>default</c> if the key does not exist.</returns>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the specified value under the given key, creating or overwriting the entry.
    /// </summary>
    /// <typeparam name="T">The type of the state to store.</typeparam>
    /// <param name="key">The unique key identifying the state entry.</param>
    /// <param name="value">The state value to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the state for the specified key atomically.
    /// If the key does not exist, <paramref name="insertValue"/> is persisted as a new entry.
    /// If the key exists, the current value is passed to <paramref name="updateFactory"/>
    /// and the returned value is persisted.
    /// </summary>
    /// <typeparam name="T">The type of the stored state.</typeparam>
    /// <param name="key">The unique key identifying the state entry.</param>
    /// <param name="insertValue">The value to persist if the key does not exist.</param>
    /// <param name="updateFactory">
    /// A function that receives the current value and returns the updated value.
    /// Only invoked if the key already exists.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask UpsertAsync<T>(string key, T insertValue, Func<T, T> updateFactory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the state entry for the specified key. This is a no-op if the key does not exist.
    /// </summary>
    /// <param name="key">The unique key identifying the state entry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a state entry exists for the specified key.
    /// </summary>
    /// <param name="key">The unique key identifying the state entry.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><c>true</c> if the key exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
