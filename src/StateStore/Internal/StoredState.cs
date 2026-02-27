namespace StateStore.Internal;

/// <summary>
/// Internal wrapper that stores state values alongside metadata.
/// This envelope format enables future versioning and migration support
/// without changing the storage format.
/// </summary>
/// <typeparam name="T">The type of the stored value.</typeparam>
internal sealed class StoredState<T>
{
    /// <summary>
    /// Gets or sets the stored value.
    /// </summary>
    public T? Value { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entry was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this entry was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the schema version of this entry.
    /// Reserved for future versioning and migration support.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Creates a new <see cref="StoredState{T}"/> for an initial insert.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <returns>A new stored state with timestamps set to now.</returns>
    public static StoredState<T> CreateNew(T value)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredState<T>
        {
            Value = value,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };
    }

    /// <summary>
    /// Creates an updated <see cref="StoredState{T}"/> preserving the original creation timestamp.
    /// </summary>
    /// <param name="value">The new value to store.</param>
    /// <param name="previous">The previous stored state to preserve metadata from.</param>
    /// <returns>An updated stored state.</returns>
    public static StoredState<T> CreateUpdated(T value, StoredState<T> previous)
    {
        return new StoredState<T>
        {
            Value = value,
            CreatedAt = previous.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = previous.Version
        };
    }
}
