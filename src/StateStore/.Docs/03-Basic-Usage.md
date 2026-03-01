# Basic Usage

This guide covers the `IStateStore` interface in detail: every method, its semantics, edge cases, and practical usage patterns.

## The IStateStore Interface

```csharp
public interface IStateStore
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync<T>(string key, T insertValue, Func<T, T> updateFactory, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
```

## GetAsync

Retrieves the state associated with a key. Returns `default(T)` (typically `null` for reference types, `0` for integers, `false` for booleans) if the key does not exist.

```csharp
// Key exists
await store.SetAsync("name", "Alice");
var name = await store.GetAsync<string>("name");
// name == "Alice"

// Key does not exist
var missing = await store.GetAsync<string>("nonexistent");
// missing == null

// Value types return default
var count = await store.GetAsync<int>("nonexistent");
// count == 0
```

**Important:** `GetAsync` does not throw when a key is missing. It returns `default`. If you need to distinguish between "key does not exist" and "key exists with a default value", use `ExistsAsync` first or use a nullable wrapper.

## SetAsync

Persists a value under the given key. If the key already exists, its value is overwritten. If the key does not exist, a new entry is created.

```csharp
// Create
await store.SetAsync("greeting", "hello");

// Overwrite
await store.SetAsync("greeting", "world");
var result = await store.GetAsync<string>("greeting");
// result == "world"
```

SetAsync works with any type that `System.Text.Json` can serialize:

```csharp
// Primitives
await store.SetAsync("count", 42);
await store.SetAsync("active", true);
await store.SetAsync("ratio", 3.14);

// Complex objects
await store.SetAsync("config", new AppConfig { Debug = true, MaxRetries = 3 });

// Collections
await store.SetAsync("tags", new List<string> { "alpha", "beta", "gamma" });

// Records
await store.SetAsync("point", new Point(10, 20));
```

## UpsertAsync

The most powerful operation in the API. `UpsertAsync` atomically inserts or updates a value:

- If the key **does not exist**, `insertValue` is persisted as a new entry.
- If the key **does exist**, the current value is passed to `updateFactory` and the returned value is persisted.

The entire read-modify-write cycle executes under a per-key exclusive lock, so concurrent callers never see a stale value.

```csharp
// Insert: key doesn't exist yet, so insertValue (0) is used
await store.UpsertAsync("counter", 0, existing => existing + 1);
var result = await store.GetAsync<int>("counter");
// result == 0 (insertValue was used because key didn't exist)

// Update: key exists, so updateFactory runs
await store.UpsertAsync("counter", 0, existing => existing + 1);
result = await store.GetAsync<int>("counter");
// result == 1 (updateFactory incremented the existing 0)
```

### Concurrent Counter Pattern

`UpsertAsync` is the correct primitive for concurrent counters. Each call is atomic at the key level:

```csharp
// 100 concurrent increments
var tasks = Enumerable.Range(0, 100)
    .Select(_ => store.UpsertAsync("counter", 1, x => x + 1).AsTask());

await Task.WhenAll(tasks);

var final = await store.GetAsync<int>("counter");
// final == 100 (guaranteed)
```

### Conditional Update Pattern

Use the factory to implement conditional updates without external locking:

```csharp
await store.UpsertAsync("session",
    new Session { Attempts = 1, LastAttempt = DateTime.UtcNow },
    existing =>
    {
        existing.Attempts++;
        existing.LastAttempt = DateTime.UtcNow;
        if (existing.Attempts > 5)
        {
            existing.LockedOut = true;
        }
        return existing;
    });
```

### Merge Pattern

Combine new data with existing state:

```csharp
var newItems = new List<string> { "item-4", "item-5" };

await store.UpsertAsync("cart",
    newItems,
    existing => existing.Concat(newItems).Distinct().ToList());
```

## DeleteAsync

Removes the entry for a key. This is a no-op if the key does not exist; it does not throw.

```csharp
await store.SetAsync("temp", "data");
await store.DeleteAsync("temp");

var exists = await store.ExistsAsync("temp");
// exists == false

// Deleting a non-existent key is safe
await store.DeleteAsync("never-existed"); // no exception
```

## ExistsAsync

Checks whether an entry exists for the given key without reading or deserializing the data. This is more efficient than `GetAsync` when you only need to check existence.

```csharp
if (await store.ExistsAsync("cache:user:123"))
{
    var user = await store.GetAsync<User>("cache:user:123");
    // use cached user
}
else
{
    // fetch from database and cache
}
```

**Note:** `ExistsAsync` bypasses the middleware pipeline and goes directly to the storage provider. This is by design: existence checks are lightweight operations that should not trigger logging, caching, or transformation middleware.

## Key Naming Conventions

Keys are plain strings with no enforced naming convention, but adopting a consistent pattern improves readability and prevents collisions:

```csharp
// Namespace pattern
"app:settings"
"user:preferences:theme"
"cache:product:12345"

// Component scoping
"dashboard:layout"
"auth:session:abc123"

// Environment scoping
"dev:feature-flags"
"prod:rate-limits"
```

### Key Constraints

- Must not be `null`, empty, or whitespace-only (throws `ArgumentException`)
- No length limit is enforced by the API, but the `FileSystemStorageProvider` hashes keys longer than 200 characters to create safe filenames
- Keys containing characters that are invalid for file names are also hashed automatically by the file system provider

## Cancellation

Every method accepts a `CancellationToken`. When the token is cancelled, the operation throws `OperationCanceledException`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    var result = await store.GetAsync<string>("slow-key", cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation timed out");
}
```

The cancellation token is respected at every level: lock acquisition, middleware pipeline, and storage provider I/O.

## Patterns and Anti-Patterns

### Pattern: Lazy Initialization

```csharp
var config = await store.GetAsync<AppConfig>("config");
if (config is null)
{
    config = AppConfig.CreateDefault();
    await store.SetAsync("config", config);
}
```

Or more concisely with `UpsertAsync`:

```csharp
await store.UpsertAsync("config",
    AppConfig.CreateDefault(),  // used if key doesn't exist
    existing => existing);       // return existing unchanged
var config = await store.GetAsync<AppConfig>("config");
```

### Anti-Pattern: External Locking

Do not wrap StateStore calls in your own locks. The library already provides per-key concurrency control:

```csharp
// BAD: Unnecessary external lock
lock (_lockObj)
{
    var value = store.GetAsync<int>("counter").AsTask().Result;
    store.SetAsync("counter", value + 1).AsTask().Wait();
}

// GOOD: Use UpsertAsync for atomic read-modify-write
await store.UpsertAsync("counter", 1, x => x + 1);
```

### Anti-Pattern: Storing Large Blobs

StateStore serializes the entire value on every write. If your state is a large object that changes frequently, consider splitting it into smaller keys:

```csharp
// BAD: Rewriting entire state on every update
await store.SetAsync("app-state", hugeObject);

// GOOD: Split into logical chunks
await store.SetAsync("app:config", hugeObject.Config);
await store.SetAsync("app:cache", hugeObject.Cache);
await store.SetAsync("app:session", hugeObject.Session);
```

## Related Guides

- [Typed State Store](04-Typed-State-Store.md) - Eliminate string keys with type-scoped storage
- [Concurrency](08-Concurrency.md) - Deep dive into the locking semantics
- [Error Handling](12-Error-Handling.md) - What exceptions to expect and how to handle them
