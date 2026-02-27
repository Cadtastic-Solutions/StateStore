using StateStore.Abstractions;
using StateStore.Internal;

namespace StateStore;

/// <summary>
/// Strongly-typed, scoped wrapper over <see cref="IStateStore"/> that derives
/// the storage key from <typeparamref name="TState"/>.
/// </summary>
/// <typeparam name="TState">The type of state being managed.</typeparam>
internal sealed class TypedStateStore<TState> : ITypedStateStore<TState>
{
    private readonly IStateStore _innerStore;
    private readonly string _key;

    /// <summary>
    /// Initializes a new instance of <see cref="TypedStateStore{TState}"/>.
    /// </summary>
    /// <param name="innerStore">The underlying state store to delegate to.</param>
    public TypedStateStore(IStateStore innerStore)
    {
        _innerStore = innerStore;
        _key = KeyHelper.DeriveKey<TState>();
    }

    /// <inheritdoc />
    public ValueTask<TState?> GetAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.GetAsync<TState>(_key, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetAsync(TState value, CancellationToken cancellationToken = default)
    {
        return _innerStore.SetAsync(_key, value, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask UpsertAsync(TState insertValue, Func<TState, TState> updateFactory, CancellationToken cancellationToken = default)
    {
        return _innerStore.UpsertAsync(_key, insertValue, updateFactory, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.DeleteAsync(_key, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        return _innerStore.ExistsAsync(_key, cancellationToken);
    }
}
