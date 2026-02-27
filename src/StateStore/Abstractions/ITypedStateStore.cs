namespace StateStore.Abstractions;

/// <summary>
/// Provides strongly-typed, scoped access to a single persisted state entry
/// where the key is derived from <typeparamref name="TState"/>.
/// </summary>
/// <typeparam name="TState">The type of the state being managed.</typeparam>
public interface ITypedStateStore<TState>
{
    /// <summary>
    /// Retrieves the persisted state for this type.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized state, or <c>default</c> if no state exists.</returns>
    ValueTask<TState?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the specified state, creating or overwriting the existing entry.
    /// </summary>
    /// <param name="value">The state value to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask SetAsync(TState value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the state atomically.
    /// If no state exists for this type, <paramref name="insertValue"/> is persisted as a new entry.
    /// If state already exists, the current value is passed to <paramref name="updateFactory"/>
    /// and the returned value is persisted.
    /// </summary>
    /// <param name="insertValue">The value to persist if no state exists.</param>
    /// <param name="updateFactory">
    /// A function that receives the current value and returns the updated value.
    /// Only invoked if state already exists.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask UpsertAsync(TState insertValue, Func<TState, TState> updateFactory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the persisted state for this type. This is a no-op if no state exists.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask DeleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether persisted state exists for this type.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><c>true</c> if state exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default);
}
