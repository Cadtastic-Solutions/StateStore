# Auto-Save

By default, StateStore persists data immediately when `SetAsync` or `UpsertAsync` is called. The auto-save system provides opt-in strategies that track which keys have been modified (dirty keys) and coordinate flush operations on configurable triggers.

## How Auto-Save Works

The auto-save system consists of three components:

1. **`IDirtyKeyTracker`** — Tracks which keys have been modified since the last flush. Every `SetAsync` and `UpsertAsync` call marks the key as dirty.

2. **`IAutoSaveStrategy`** — Defines when a flush should occur. The library ships with two strategies:
   - `PeriodicAutoSaveStrategy` — Flushes on a timer interval
   - `ShutdownAutoSaveStrategy` — Flushes when the host application is stopping

3. **`AutoSaveHostedService`** — An `IHostedService` that manages the lifecycle of registered strategies and executes flush operations.

### Flush Behavior

When a flush is triggered, the auto-save service:

1. Drains all dirty keys from the tracker (atomically)
2. For each dirty key, verifies that the state is persisted
3. If verification fails for a key, the key is re-marked as dirty for retry on the next flush cycle
4. Errors are logged but never crash the host

Because `SetAsync` and `UpsertAsync` persist data immediately by default, the auto-save flush primarily serves as a verification and safety net mechanism. In future releases, this infrastructure may support deferred-write strategies where data is buffered in memory and flushed to storage only during auto-save cycles.

## Enabling Auto-Save

Auto-save requires the DI-hosted application model (`IHostedService`). It is configured through the `UseAutoSave` method:

```csharp
services.AddStateStore(options =>
{
    options.UseFileSystem(fs => fs.BasePath = "./state");
    options.UseJsonSerializer();
    options.UseAutoSave(auto =>
    {
        auto.AddPeriodic(TimeSpan.FromMinutes(5));
        auto.AddShutdown();
    });
});
```

## PeriodicAutoSaveStrategy

Flushes dirty keys on a recurring timer. The interval is configurable with a minimum of 1 second.

```csharp
options.UseAutoSave(auto =>
{
    auto.AddPeriodic(TimeSpan.FromMinutes(5));  // Every 5 minutes
});
```

### How It Works

1. On `StartAsync`, creates a `PeriodicTimer` with the configured interval
2. On each tick, invokes the flush callback provided by `AutoSaveHostedService`
3. On `StopAsync`, cancels the timer and waits for any in-progress flush to complete

### Interval Guidelines

| Scenario | Suggested Interval |
|----------|--------------------|
| Configuration state (changes rarely) | 5-15 minutes |
| User session data | 1-5 minutes |
| Analytics counters | 30 seconds - 2 minutes |
| High-frequency updates | Use explicit persistence instead |

The minimum interval is 1 second. Passing a value less than 1 second throws `ArgumentOutOfRangeException`.

## ShutdownAutoSaveStrategy

Flushes dirty keys when the application is shutting down. This ensures no modified state is lost during a graceful shutdown.

```csharp
options.UseAutoSave(auto =>
{
    auto.AddShutdown();
});
```

### How It Works

1. On `StartAsync`, registers a callback on `IHostApplicationLifetime.ApplicationStopping`
2. When the stopping event fires, invokes the flush callback synchronously
3. `AutoSaveHostedService.StopAsync` also performs a final flush as an additional safety measure

This strategy is designed for graceful shutdowns. If the process is killed forcefully (e.g., `kill -9`), the callback will not execute. For critical state, combine with periodic auto-save.

## Composing Strategies

Strategies are composable. Register multiple strategies, and all will execute independently:

```csharp
options.UseAutoSave(auto =>
{
    auto.AddPeriodic(TimeSpan.FromMinutes(5));  // Regular flushes
    auto.AddShutdown();                          // Final flush on shutdown
});
```

This is the recommended configuration for production applications: periodic flushes protect against data loss from crashes, and the shutdown strategy ensures a clean final flush.

## Dirty Key Tracking

The `DirtyKeyTracker` uses a `ConcurrentDictionary` internally. Its two operations are:

- **`MarkDirty(key)`** — Adds the key to the dirty set. Called automatically by `SetAsync` and `UpsertAsync`.
- **`DrainDirtyKeys()`** — Atomically removes and returns all dirty keys. Called during each flush cycle.

The drain operation is atomic: keys that become dirty during a flush are captured in the next cycle, not the current one.

## Error Handling

Auto-save is designed to be resilient:

- If a flush fails for a specific key, the error is logged via `ILogger<AutoSaveHostedService>` at `Error` level
- The failed key is re-marked as dirty for retry on the next cycle
- The host application is never crashed by auto-save errors
- Other keys in the same flush batch continue processing even if one key fails

```
[Error] Failed to verify flush for key 'user:preferences'. Key will be retried on next cycle.
System.IO.IOException: Disk full
```

## Lifecycle

The auto-save hosted service follows the standard `IHostedService` lifecycle:

```
Host Starting
  └─→ AutoSaveHostedService.StartAsync()
        ├─→ PeriodicAutoSaveStrategy.StartAsync()    → Timer starts
        └─→ ShutdownAutoSaveStrategy.StartAsync()    → Stopping callback registered

Host Running
        └─→ PeriodicAutoSaveStrategy ticks           → FlushDirtyKeys()

Host Stopping
  ├─→ ShutdownAutoSaveStrategy callback fires         → FlushDirtyKeys()
  └─→ AutoSaveHostedService.StopAsync()
        ├─→ PeriodicAutoSaveStrategy.StopAsync()      → Timer cancelled
        ├─→ ShutdownAutoSaveStrategy.StopAsync()      → Registration disposed
        └─→ Final FlushDirtyKeys()                    → Last safety flush

Host Stopped
  └─→ AutoSaveHostedService.Dispose()
        ├─→ PeriodicAutoSaveStrategy.Dispose()
        └─→ ShutdownAutoSaveStrategy.Dispose()
```

## When NOT to Use Auto-Save

Auto-save is not needed when:

- You call `SetAsync`/`UpsertAsync` and data must be persisted immediately (it already is by default)
- You are using `InMemoryStorageProvider` (data is never durable)
- You are building a library and don't control the host lifecycle
- Your application doesn't use `Microsoft.Extensions.Hosting`

For standalone (non-hosted) usage, consider calling your own flush logic explicitly at application shutdown.

## Implementing Custom Auto-Save Strategies

Implement `IAutoSaveStrategy` for custom triggers:

```csharp
public sealed class EventDrivenAutoSaveStrategy : IAutoSaveStrategy
{
    private Func<CancellationToken, Task>? _flushAsync;
    private bool _disposed;

    public Task StartAsync(Func<CancellationToken, Task> flushAsync, CancellationToken cancellationToken)
    {
        _flushAsync = flushAsync;
        return Task.CompletedTask;
    }

    // Call this from your application code when a save should occur
    public async Task TriggerSaveAsync(CancellationToken cancellationToken = default)
    {
        if (_flushAsync is not null)
        {
            await _flushAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
```

## Related Guides

- [Dependency Injection](10-Dependency-Injection.md) - Registering auto-save with DI
- [Error Handling](12-Error-Handling.md) - Auto-save error handling behavior
- [Extensibility](14-Extensibility.md) - Building custom auto-save strategies
- [Core Concepts](02-Core-Concepts.md) - Overall architecture
