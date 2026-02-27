using System.Collections.Concurrent;

namespace StateStore.Internal;

/// <summary>
/// Thread-safe implementation of <see cref="IDirtyKeyTracker"/> using a concurrent collection.
/// </summary>
internal sealed class DirtyKeyTracker : IDirtyKeyTracker
{
    private readonly ConcurrentDictionary<string, byte> _dirtyKeys = new();

    /// <inheritdoc />
    public void MarkDirty(string key)
    {
        _dirtyKeys.TryAdd(key, 0);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DrainDirtyKeys()
    {
        var keys = _dirtyKeys.Keys.ToArray();
        foreach (var key in keys)
        {
            _dirtyKeys.TryRemove(key, out _);
        }

        return keys;
    }
}
