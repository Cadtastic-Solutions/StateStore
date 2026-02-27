# Extensibility

StateStore is designed from the ground up to be extended. Every core behavior sits behind an interface, and the library provides clear extension points for storage backends, serializers, middleware, and auto-save strategies.

## Extension Points

| Interface | Purpose | Built-In Implementations |
|-----------|---------|--------------------------|
| `IStorageProvider` | Raw byte I/O against a storage backend | `InMemoryStorageProvider`, `FileSystemStorageProvider` |
| `IStateSerializer` | Convert typed objects to/from bytes | `JsonStateSerializer` |
| `IStateStoreMiddleware` | Intercept read/write/delete operations | `LoggingMiddleware` |
| `IAutoSaveStrategy` | Define when dirty state should be flushed | `PeriodicAutoSaveStrategy`, `ShutdownAutoSaveStrategy` |

## Custom Storage Providers

### When to Build One

- You need a specific storage backend (SQLite, Redis, Azure Blob Storage, AWS S3, etc.)
- You want to store state in a database alongside other application data
- You need a provider with specific durability or performance characteristics

### Implementation Guide

```csharp
public sealed class RedisStorageProvider : IStorageProvider
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;

    public RedisStorageProvider(IConnectionMultiplexer redis, string prefix = "statestore:")
    {
        _redis = redis;
        _prefix = prefix;
    }

    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(_prefix + key);
        return value.HasValue ? (byte[])value! : null;
    }

    public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(_prefix + key, data.ToArray());
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(_prefix + key);
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(_prefix + key);
    }
}
```

### Provider Implementation Checklist

1. **Return `null` from `ReadAsync` when a key doesn't exist** — never throw for missing keys
2. **`DeleteAsync` must be a no-op for missing keys** — never throw
3. **Respect `CancellationToken`** — pass it to underlying async operations where supported; call `cancellationToken.ThrowIfCancellationRequested()` for sync operations
4. **Be thread-safe** — StateStore provides per-key locking, but the provider must be safe for concurrent calls on different keys
5. **Return `ValueTask`** — use `new ValueTask<T>(result)` for sync completions to avoid `Task` allocations
6. **Handle `ReadOnlyMemory<byte>`** — call `.ToArray()` if the underlying API requires `byte[]`

### Registration

```csharp
// DI: Register before AddStateStore (TryAdd won't override)
services.AddSingleton<IStorageProvider>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return new RedisStorageProvider(redis);
});
services.AddStateStore();

// Builder
var store = new StateStoreBuilder()
    .UseProvider(new RedisStorageProvider(redis))
    .UseJsonSerializer()
    .Build();
```

## Custom Serializers

### When to Build One

- You need a binary format (MessagePack, Protobuf, FlatBuffers)
- You need AOT compatibility with source-generated JSON
- You need encryption at the serialization level
- You want compressed storage

### Implementation Guide

```csharp
public sealed class CompressedJsonSerializer : IStateSerializer
{
    private readonly JsonStateSerializer _inner = new();

    public byte[] Serialize<T>(T value)
    {
        var json = _inner.Serialize(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(json);
        }
        return output.ToArray();
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return _inner.Deserialize<T>(output.ToArray());
    }
}
```

### Serializer Implementation Checklist

1. **Wrap exceptions in `StateSerializationException`** — include the `TargetType` and the original exception as `InnerException`
2. **Handle `ReadOnlySpan<byte>`** — call `.ToArray()` if the underlying API doesn't support spans
3. **Be stateless or thread-safe** — the serializer is called concurrently from multiple threads
4. **Remember the `StoredState<T>` envelope** — the serializer receives `StoredState<T>`, not `T` directly

### Registration

```csharp
// DI
services.AddSingleton<IStateSerializer, CompressedJsonSerializer>();
services.AddStateStore();

// Builder
var store = new StateStoreBuilder()
    .UseSerializer(new CompressedJsonSerializer())
    .UseInMemory()
    .Build();
```

## Custom Middleware

### When to Build One

- **Logging:** Custom log format or destination
- **Metrics:** Emit counters/histograms to Prometheus, OpenTelemetry, etc.
- **Caching:** In-memory cache layer to reduce storage reads
- **Encryption:** Encrypt data at rest (between serialization and storage)
- **Compression:** Compress serialized data before storage
- **Validation:** Enforce size limits, key patterns, or data schemas
- **Rate limiting:** Throttle operations per key or globally
- **Auditing:** Record who changed what and when

### Implementation Pattern

Every middleware follows the same pattern: do work before calling `next()`, call `next()`, do work after.

```csharp
public sealed class EncryptionMiddleware : IStateStoreMiddleware
{
    private readonly byte[] _key;

    public EncryptionMiddleware(byte[] encryptionKey)
    {
        _key = encryptionKey;
    }

    public async ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
    {
        var encrypted = await next().ConfigureAwait(false);
        if (encrypted is null) return null;
        return Decrypt(encrypted);
    }

    public async ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        // Encrypt before writing
        var encrypted = Encrypt(data.ToArray());
        // Note: We can't easily replace the data in the pipeline in the current design.
        // For encryption, consider implementing it at the serializer level instead.
        await next().ConfigureAwait(false);
    }

    public ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
        => next();

    private byte[] Encrypt(byte[] data) { /* AES encryption */ }
    private byte[] Decrypt(byte[] data) { /* AES decryption */ }
}
```

### Middleware Implementation Checklist

1. **Always call `next()`** unless intentionally short-circuiting
2. **Use `ConfigureAwait(false)`** on all `await` calls
3. **For passthrough operations, return `next()` directly** — avoid unnecessary async state machines
4. **Catch only exceptions you intend to handle** — let others propagate naturally
5. **Be thread-safe** — middleware is called concurrently from multiple threads
6. **Keep middleware focused** — one concern per middleware component

## Custom Auto-Save Strategies

### When to Build One

- You want to flush on a custom trigger (file system watcher, message queue event, signal)
- You need conditional flushing (only flush if certain criteria are met)
- You want to integrate with an external scheduling system

### Implementation Guide

```csharp
public sealed class FileWatcherAutoSaveStrategy : IAutoSaveStrategy
{
    private readonly string _watchPath;
    private FileSystemWatcher? _watcher;
    private Func<CancellationToken, Task>? _flushAsync;
    private bool _disposed;

    public FileWatcherAutoSaveStrategy(string watchPath)
    {
        _watchPath = watchPath;
    }

    public Task StartAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken)
    {
        _flushAsync = flushAsync;
        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            Filter = "*.trigger"
        };
        _watcher.Changed += async (_, _) =>
        {
            if (_flushAsync is not null)
            {
                await _flushAsync(CancellationToken.None);
            }
        };
        _watcher.EnableRaisingEvents = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
```

### Strategy Implementation Checklist

1. **Implement `IDisposable` correctly** — clean up timers, watchers, registrations
2. **Handle `StopAsync` gracefully** — stop triggering flushes and wait for in-progress flushes
3. **Never crash the host** — catch and log exceptions in flush callbacks
4. **Store the `flushAsync` callback** from `StartAsync` and invoke it when your trigger fires

## Architecture for Extensibility

### Composition over Inheritance

StateStore uses composition throughout. To add behavior, you compose implementations rather than subclass:

```csharp
// Compose a caching layer with logging
var store = new StateStoreBuilder()
    .UseFileSystem()
    .UseJsonSerializer()
    .UseMiddleware(new LoggingMiddleware(logger))    // Logs all operations
    .UseMiddleware(new CacheMiddleware())             // Caches reads in memory
    .Build();
```

### Decoration Pattern

You can decorate any interface to add cross-cutting behavior:

```csharp
public sealed class RetryingStorageProvider : IStorageProvider
{
    private readonly IStorageProvider _inner;
    private readonly int _maxRetries;

    public RetryingStorageProvider(IStorageProvider inner, int maxRetries = 3)
    {
        _inner = inner;
        _maxRetries = maxRetries;
    }

    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _inner.ReadAsync(key, cancellationToken);
            }
            catch when (attempt < _maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }
    }

    // ... same pattern for WriteAsync, DeleteAsync, ExistsAsync
}
```

Register the decorator:

```csharp
var store = new StateStoreBuilder()
    .UseProvider(new RetryingStorageProvider(
        new FileSystemStorageProvider(new FileSystemStorageOptions { BasePath = "./state" }),
        maxRetries: 3))
    .UseJsonSerializer()
    .Build();
```

### Interface Segregation

All StateStore interfaces have at most 5 methods, keeping them easy to implement and mock:

| Interface | Method Count |
|-----------|-------------|
| `IStorageProvider` | 4 |
| `IStateStore` | 5 |
| `ITypedStateStore<T>` | 5 |
| `IStateSerializer` | 2 |
| `IStateStoreMiddleware` | 3 |
| `IAutoSaveStrategy` | 2 + `IDisposable` |

## Future Extensibility

StateStore's internal `StoredState<T>` envelope includes a `Version` field (currently always `1`) that is reserved for future schema migration support. When versioning is introduced:

- Existing stored data will remain compatible (the field already exists)
- Migration functions will transform data between versions during deserialization
- No storage format changes will be required

## Related Guides

- [Storage Providers](05-Storage-Providers.md) - Built-in provider details
- [Serialization](06-Serialization.md) - Built-in serializer details
- [Middleware](07-Middleware.md) - Middleware pipeline details
- [Auto-Save](09-Auto-Save.md) - Auto-save strategy details
- [Core Concepts](02-Core-Concepts.md) - Architectural overview
