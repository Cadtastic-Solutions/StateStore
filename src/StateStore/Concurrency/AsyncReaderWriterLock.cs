namespace StateStore.Concurrency;

/// <summary>
/// A lightweight async reader-writer lock that supports concurrent readers
/// and exclusive writers. All operations are async-friendly and respect cancellation.
/// </summary>
internal sealed class AsyncReaderWriterLock
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readerCountLock = new(1, 1);
    private int _readerCount;

    /// <summary>
    /// Acquires a shared read lock. Multiple readers can hold the lock concurrently.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel lock acquisition.</param>
    public async ValueTask EnterReadLockAsync(CancellationToken cancellationToken)
    {
        await _readerCountLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _readerCount++;
            if (_readerCount == 1)
            {
                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _readerCountLock.Release();
        }
    }

    /// <summary>
    /// Releases a shared read lock.
    /// </summary>
    public void ExitReadLock()
    {
        _readerCountLock.Wait();
        try
        {
            _readerCount--;
            if (_readerCount == 0)
            {
                _writeLock.Release();
            }
        }
        finally
        {
            _readerCountLock.Release();
        }
    }

    /// <summary>
    /// Acquires an exclusive write lock. No readers or other writers can proceed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel lock acquisition.</param>
    public async ValueTask EnterWriteLockAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases an exclusive write lock.
    /// </summary>
    public void ExitWriteLock()
    {
        _writeLock.Release();
    }
}
