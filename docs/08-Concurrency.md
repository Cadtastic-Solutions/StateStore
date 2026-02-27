# Concurrency

StateStore is designed to be used safely from multiple threads without requiring consumers to manage synchronization. This guide explains the concurrency model, the locking strategy, and the guarantees provided by each operation.

## Concurrency Model Overview

StateStore uses **per-key async reader-writer locks**. This provides three key properties:

1. **Concurrent readers** — Multiple threads can read the same key simultaneously
2. **Exclusive writers** — Write operations acquire an exclusive lock per key, blocking other writers and readers for that key
3. **Key independence** — Operations on different keys never block each other

```
Key "A":  Read ─────────────  (concurrent reads allowed)
          Read ─────────────

Key "A":  Write ████████████  (exclusive: no other ops on "A")

Key "B":  Write ████████████  (independent: "A" and "B" don't block each other)
Key "A":  Read ─────────────
```

## Operation Locking Semantics

| Operation | Lock Type | Scope | Blocks |
|-----------|----------|-------|--------|
| `GetAsync` | Shared (read) | Per key | Nothing (concurrent reads allowed) |
| `ExistsAsync` | Shared (read) | Per key | Nothing |
| `SetAsync` | Exclusive (write) | Per key | Other reads/writes to same key |
| `UpsertAsync` | Exclusive (write) | Per key | Other reads/writes to same key |
| `DeleteAsync` | Exclusive (write) | Per key | Other reads/writes to same key |

## The KeyedAsyncLock

Internally, StateStore uses a `KeyedAsyncLock` — a collection of per-key `AsyncReaderWriterLock` instances. Each `AsyncReaderWriterLock` is a lightweight async-compatible reader-writer lock built on two `SemaphoreSlim` instances.

### Reference Counting and Eviction

Per-key locks are reference-counted. When no operations are active for a key, its lock entry is evicted from the collection. This prevents unbounded memory growth in scenarios with many unique keys:

```
Operations:
  1. AcquireWriteLock("key-1")   → Creates lock entry, refCount = 1
  2. AcquireReadLock("key-1")    → Reuses lock entry, refCount = 2
  3. Release read lock            → refCount = 1
  4. Release write lock           → refCount = 0, entry evicted
```

### No Global Lock

The library deliberately avoids a global lock. A write to key `"A"` never blocks a read from key `"B"`, regardless of how many concurrent operations are in flight. This is critical for applications with high concurrency across many keys.

## UpsertAsync Atomicity

`UpsertAsync` is the most concurrency-sensitive operation. It performs a read-modify-write cycle that must be atomic to prevent lost updates:

```
UpsertAsync("counter", 1, x => x + 1):

  1. Acquire exclusive write lock for "counter"
  2. Read current value from storage
  3. If exists: apply updateFactory (x => x + 1)
     If not exists: use insertValue (1)
  4. Write new value to storage
  5. Release write lock
```

Steps 2-4 execute within the same lock scope. No other thread can read or write `"counter"` between the read and the write.

### Concurrent Upsert Example

Consider 100 concurrent tasks incrementing the same counter:

```csharp
await store.SetAsync("counter", 0);

var tasks = Enumerable.Range(0, 100)
    .Select(_ => store.UpsertAsync("counter", 1, x => x + 1).AsTask());

await Task.WhenAll(tasks);

var result = await store.GetAsync<int>("counter");
// result == 100 (guaranteed by per-key locking)
```

Without per-key locking, this would be a classic lost-update problem where concurrent reads of the same value overwrite each other's increments. StateStore's locking guarantees sequential execution per key.

## Cancellation and Locking

All lock acquisitions respect the `CancellationToken` passed by the caller. If the token is cancelled while waiting for a lock, the operation throws `OperationCanceledException` without modifying state:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

try
{
    // If the lock for "busy-key" is held for more than 2 seconds,
    // this will throw OperationCanceledException
    await store.SetAsync("busy-key", "value", cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Lock acquisition timed out");
}
```

## Concurrent Access to Different Keys

Operations on different keys execute in parallel with no contention:

```csharp
// These run concurrently with no blocking between them
var tasks = Enumerable.Range(0, 50)
    .Select(i => store.SetAsync($"key_{i}", i).AsTask());

await Task.WhenAll(tasks);

// All 50 keys are written correctly
for (var i = 0; i < 50; i++)
{
    var result = await store.GetAsync<int>($"key_{i}");
    // result == i (each key is independent)
}
```

## Thread Safety at Each Layer

| Layer | Thread Safety Mechanism |
|-------|------------------------|
| `IStateStore` | Per-key `AsyncReaderWriterLock` |
| Middleware Pipeline | Thread-safe by design (stateless `next` delegates) |
| `InMemoryStorageProvider` | `ConcurrentDictionary` |
| `FileSystemStorageProvider` | File I/O protected by StateStore's lock layer |
| `JsonStateSerializer` | Stateless (thread-safe `JsonSerializerOptions` instance) |
| Dirty Key Tracker | `ConcurrentDictionary` |

## Anti-Patterns

### External Locking

StateStore manages all synchronization internally. Adding your own locks creates unnecessary contention and risks deadlocks:

```csharp
// BAD: Double locking
private readonly SemaphoreSlim _lock = new(1, 1);

await _lock.WaitAsync();
try
{
    var value = await store.GetAsync<int>("counter");
    await store.SetAsync("counter", value + 1);
}
finally
{
    _lock.Release();
}

// GOOD: Use UpsertAsync
await store.UpsertAsync("counter", 1, x => x + 1);
```

### Manual Read-Modify-Write

Never read and write separately when the write depends on the read value. The gap between `GetAsync` and `SetAsync` is not atomic:

```csharp
// BAD: Race condition between Get and Set
var count = await store.GetAsync<int>("counter");
await store.SetAsync("counter", count + 1); // Another thread may have changed it

// GOOD: Atomic upsert
await store.UpsertAsync("counter", 1, x => x + 1);
```

### Blocking on ValueTask

Never use `.Result` or `.Wait()` on `ValueTask`. Convert to `Task` first if you need to block:

```csharp
// BAD: May deadlock or behave incorrectly
var result = store.GetAsync<int>("key").Result;

// ACCEPTABLE (but prefer async throughout)
var result = store.GetAsync<int>("key").AsTask().Result;

// BEST: Use async all the way
var result = await store.GetAsync<int>("key");
```

## Performance Considerations

### Lock Granularity

Per-key locking provides fine-grained concurrency. If your application accesses thousands of different keys concurrently, the contention is spread across independent locks. If most operations target a small set of hot keys, those specific keys become serialization points.

### Reader-Writer Asymmetry

If your workload is read-heavy, the reader-writer lock provides significant throughput benefits over a simple mutex. Multiple readers execute concurrently, and only writes create contention.

### Lock Overhead

Each unique key that has active operations maintains a small `AsyncReaderWriterLock` in memory (two `SemaphoreSlim` instances). These are evicted when no operations reference the key. For applications with millions of unique keys accessed in bursts, the overhead is negligible because locks only exist for actively-contended keys.

## Related Guides

- [Core Concepts](02-Core-Concepts.md) - Concurrency in the architectural context
- [Basic Usage](03-Basic-Usage.md) - UpsertAsync patterns
- [Error Handling](12-Error-Handling.md) - `StateStoreConcurrencyException`
- [Testing](13-Testing.md) - Writing concurrency tests
