using System.Collections.Concurrent;

namespace StateStore.Concurrency;

/// <summary>
/// Provides per-key async reader-writer locking.
/// Read operations can proceed concurrently for the same key.
/// Write operations are exclusive per key. Operations on different keys never block each other.
/// Uses reference counting with eviction to prevent unbounded memory growth.
/// </summary>
internal sealed class KeyedAsyncLock
{
    private readonly ConcurrentDictionary<string, RefCountedLock> _locks = new();

    /// <summary>
    /// Acquires a shared read lock for the specified key.
    /// Multiple readers can hold the lock concurrently.
    /// </summary>
    /// <param name="key">The key to lock.</param>
    /// <param name="cancellationToken">A token to cancel lock acquisition.</param>
    /// <returns>A disposable that releases the read lock when disposed.</returns>
    public async ValueTask<IDisposable> AcquireReadLockAsync(string key, CancellationToken cancellationToken)
    {
        var entry = GetOrCreateEntry(key);
        try
        {
            await entry.Lock.EnterReadLockAsync(cancellationToken).ConfigureAwait(false);
            return new LockReleaser(this, key, entry, isWrite: false);
        }
        catch
        {
            ReleaseEntry(key, entry);
            throw;
        }
    }

    /// <summary>
    /// Acquires an exclusive write lock for the specified key.
    /// No other readers or writers can hold the lock while a write lock is held.
    /// </summary>
    /// <param name="key">The key to lock.</param>
    /// <param name="cancellationToken">A token to cancel lock acquisition.</param>
    /// <returns>A disposable that releases the write lock when disposed.</returns>
    public async ValueTask<IDisposable> AcquireWriteLockAsync(string key, CancellationToken cancellationToken)
    {
        var entry = GetOrCreateEntry(key);
        try
        {
            await entry.Lock.EnterWriteLockAsync(cancellationToken).ConfigureAwait(false);
            return new LockReleaser(this, key, entry, isWrite: true);
        }
        catch
        {
            ReleaseEntry(key, entry);
            throw;
        }
    }

    private RefCountedLock GetOrCreateEntry(string key)
    {
        while (true)
        {
            var entry = _locks.GetOrAdd(key, static _ => new RefCountedLock());
            lock (entry)
            {
                if (entry.IsEvicted)
                {
                    continue;
                }

                entry.RefCount++;
                return entry;
            }
        }
    }

    private void ReleaseEntry(string key, RefCountedLock entry)
    {
        lock (entry)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                entry.IsEvicted = true;
                _locks.TryRemove(key, out _);
            }
        }
    }

    private sealed class RefCountedLock
    {
        public AsyncReaderWriterLock Lock { get; } = new();
        public int RefCount { get; set; }
        public bool IsEvicted { get; set; }
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly KeyedAsyncLock _parent;
        private readonly string _key;
        private readonly RefCountedLock _entry;
        private readonly bool _isWrite;
        private bool _disposed;

        public LockReleaser(KeyedAsyncLock parent, string key, RefCountedLock entry, bool isWrite)
        {
            _parent = parent;
            _key = key;
            _entry = entry;
            _isWrite = isWrite;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_isWrite)
            {
                _entry.Lock.ExitWriteLock();
            }
            else
            {
                _entry.Lock.ExitReadLock();
            }

            _parent.ReleaseEntry(_key, _entry);
        }
    }
}
