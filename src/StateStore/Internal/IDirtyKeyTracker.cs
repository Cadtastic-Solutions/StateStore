namespace StateStore.Internal;

/// <summary>
/// Tracks keys that have been modified since the last auto-save flush.
/// </summary>
internal interface IDirtyKeyTracker
{
    /// <summary>
    /// Marks a key as dirty (modified since last flush).
    /// </summary>
    /// <param name="key">The key to mark as dirty.</param>
    void MarkDirty(string key);

    /// <summary>
    /// Returns and clears all dirty keys atomically.
    /// </summary>
    /// <returns>The set of keys that were dirty.</returns>
    IReadOnlyCollection<string> DrainDirtyKeys();
}
