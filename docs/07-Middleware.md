# Middleware

StateStore includes a middleware pipeline modeled after ASP.NET Core's middleware pattern. Middleware components sit between the serialization layer and the storage provider, allowing you to intercept, transform, or short-circuit every read, write, and delete operation.

## The IStateStoreMiddleware Interface

```csharp
public interface IStateStoreMiddleware
{
    ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken);
    ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken);
    ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken);
}
```

Each method receives:
- The **key** being operated on
- A **`next`** delegate that invokes the next middleware in the chain (or the storage provider if this is the last middleware)
- A **`CancellationToken`** for cooperative cancellation
- For writes, the **data** being written (already serialized to bytes)

## How the Pipeline Works

Middleware components execute in **registration order** for the forward pass and in **reverse order** for the return pass, forming a nested call stack:

```
Request flow:

  Client → M1.Before → M2.Before → StorageProvider
  Client ← M1.After  ← M2.After  ← StorageProvider
```

For a pipeline with two middleware components registered as `[M1, M2]`:

1. `M1.OnReadAsync` starts executing
2. `M1` calls `next()` which invokes `M2.OnReadAsync`
3. `M2` calls `next()` which invokes `StorageProvider.ReadAsync`
4. The result propagates back through `M2`, then `M1`

This is the same pattern used by ASP.NET Core's request pipeline, OWIN, and many other middleware-based frameworks.

### Short-Circuiting

A middleware can skip the rest of the pipeline by not calling `next()`:

```csharp
public ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
{
    if (_cache.TryGetValue(key, out var cached))
    {
        return new ValueTask<byte[]?>(cached); // Short-circuit: never calls next()
    }

    return next(); // Continue to next middleware / storage provider
}
```

### ExistsAsync Bypasses Middleware

The `ExistsAsync` operation goes directly to the storage provider without passing through the middleware pipeline. This is a deliberate design decision: existence checks are lightweight operations that should not trigger logging, caching, or transformation side effects.

## Built-in: LoggingMiddleware

StateStore ships with `LoggingMiddleware` that logs every operation at `Debug` level. It serves as both a useful diagnostic tool and a reference implementation for middleware authors.

```csharp
// Registration
services.AddStateStore(options =>
{
    options.UseMiddleware<LoggingMiddleware>();
});
```

Log output (at Debug level):

```
StateStore reading key 'user:theme'
StateStore read key 'user:theme': 42 bytes
StateStore writing key 'user:theme' (56 bytes)
StateStore wrote key 'user:theme' successfully
StateStore deleting key 'temp-cache'
StateStore deleted key 'temp-cache' successfully
```

`LoggingMiddleware` requires `ILogger<LoggingMiddleware>` to be available in the DI container, which is standard in any application using `Microsoft.Extensions.Logging`.

## Writing Custom Middleware

### Example: Timing Middleware

```csharp
public sealed class TimingMiddleware : IStateStoreMiddleware
{
    private readonly ILogger<TimingMiddleware> _logger;

    public TimingMiddleware(ILogger<TimingMiddleware> logger)
    {
        _logger = logger;
    }

    public async ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = await next().ConfigureAwait(false);
        sw.Stop();
        _logger.LogDebug("Read '{Key}' completed in {ElapsedMs}ms", key, sw.ElapsedMilliseconds);
        return result;
    }

    public async ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await next().ConfigureAwait(false);
        sw.Stop();
        _logger.LogDebug("Write '{Key}' ({Size} bytes) completed in {ElapsedMs}ms", key, data.Length, sw.ElapsedMilliseconds);
    }

    public async ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await next().ConfigureAwait(false);
        sw.Stop();
        _logger.LogDebug("Delete '{Key}' completed in {ElapsedMs}ms", key, sw.ElapsedMilliseconds);
    }
}
```

### Example: In-Memory Cache Middleware

```csharp
public sealed class CacheMiddleware : IStateStoreMiddleware
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public async ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;  // Short-circuit: return from cache
        }

        var result = await next().ConfigureAwait(false);

        if (result is not null)
        {
            _cache[key] = result;
        }

        return result;
    }

    public async ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        await next().ConfigureAwait(false);
        _cache[key] = data.ToArray();  // Update cache after successful write
    }

    public async ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        await next().ConfigureAwait(false);
        _cache.TryRemove(key, out _);  // Evict from cache after delete
    }
}
```

### Example: Validation Middleware

```csharp
public sealed class MaxSizeMiddleware : IStateStoreMiddleware
{
    private readonly int _maxBytes;

    public MaxSizeMiddleware(int maxBytes = 1_048_576) // 1 MB default
    {
        _maxBytes = maxBytes;
    }

    public ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
    {
        return next();
    }

    public ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        if (data.Length > _maxBytes)
        {
            throw new InvalidOperationException(
                $"State for key '{key}' exceeds maximum size of {_maxBytes} bytes (actual: {data.Length} bytes).");
        }

        return next();
    }

    public ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
    {
        return next();
    }
}
```

## Registration

### Via DI (Generic Type)

```csharp
services.AddStateStore(options =>
{
    options.UseMiddleware<LoggingMiddleware>();
    options.UseMiddleware<TimingMiddleware>();
});
```

### Via DI (Pipeline Builder)

```csharp
services.AddStateStore(options =>
{
    options.UseMiddleware(pipeline =>
    {
        pipeline.Add<LoggingMiddleware>();
        pipeline.Add<TimingMiddleware>();
    });
});
```

### Via Standalone Builder

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .UseMiddleware(new CacheMiddleware())
    .UseMiddleware(new MaxSizeMiddleware(maxBytes: 512_000))
    .Build();
```

### Registration Order Matters

Middleware executes in registration order. For the typical use case:

```csharp
options.UseMiddleware<LoggingMiddleware>();   // 1st: logs before/after everything
options.UseMiddleware<CacheMiddleware>();     // 2nd: cache check before hitting storage
options.UseMiddleware<TimingMiddleware>();    // 3rd: times only the storage provider call
```

## Middleware and Data Flow

Middleware operates on **serialized bytes**, not on typed objects. When `OnWriteAsync` receives the `data` parameter, it has already been serialized through `IStateSerializer`. This means:

- Middleware cannot easily inspect the typed value being stored (it would need to deserialize the bytes)
- Middleware is format-agnostic: the same middleware works regardless of whether JSON, MessagePack, or any other serializer is used
- Encryption and compression middleware operate naturally at this level

## Error Handling in Middleware

If a middleware throws an unhandled exception (one that is not a `StateStoreException` or `OperationCanceledException`), it is caught by the pipeline and wrapped in a `MiddlewareException`:

```csharp
try
{
    await store.GetAsync<string>("key");
}
catch (MiddlewareException ex)
{
    Console.WriteLine(ex.MiddlewareType); // typeof(MyBrokenMiddleware)
    Console.WriteLine(ex.InnerException); // The original exception
}
```

See: [Error Handling](12-Error-Handling.md)

## Guidelines for Middleware Authors

1. **Always call `next()`** unless you are intentionally short-circuiting. Forgetting to call `next()` on write operations means data is never persisted.

2. **Use `ConfigureAwait(false)`** on all `await` calls. StateStore is a library; middleware should not capture the synchronization context.

3. **Handle exceptions carefully.** Exceptions thrown by `next()` propagate naturally. Only catch them if you need to log, transform, or suppress them.

4. **Keep middleware focused.** Each middleware should do one thing. Compose multiple focused middleware components rather than building one that does everything.

5. **Be mindful of async overhead.** If your middleware has no async work for a particular operation, return the `next()` delegate directly without `async/await`:

    ```csharp
    // GOOD: No async overhead when there's nothing to do
    public ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken ct)
        => next();

    // UNNECESSARY: Async state machine for a simple passthrough
    public async ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken ct)
        => await next();
    ```

## Related Guides

- [Core Concepts](02-Core-Concepts.md) - Where middleware fits in the architecture
- [Error Handling](12-Error-Handling.md) - `MiddlewareException` details
- [Extensibility](14-Extensibility.md) - Advanced middleware patterns
- [Dependency Injection](10-Dependency-Injection.md) - Registering middleware with DI
