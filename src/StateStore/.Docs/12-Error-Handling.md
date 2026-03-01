# Error Handling

StateStore defines a typed exception hierarchy that provides structured context for every failure mode. All exceptions inherit from `StateStoreException`, making it straightforward to catch all library errors or handle specific failure types.

## Exception Hierarchy

```
Exception
  └── StateStoreException                  Base for all StateStore errors
        ├── StateSerializationException    Serialization/deserialization failure
        ├── StorageProviderException       Storage backend failure
        ├── StateStoreConcurrencyException Lock-related failure
        └── MiddlewareException            Middleware component failure
```

## StateStoreException

The base exception for all StateStore errors. Catch this to handle any library-level failure:

```csharp
try
{
    await store.SetAsync("key", value);
}
catch (StateStoreException ex)
{
    // Handles any StateStore error
    logger.LogError(ex, "State store operation failed");
}
```

## StateSerializationException

Thrown when serialization or deserialization fails. This typically happens when:

- A type cannot be serialized by the configured serializer (e.g., circular references, unsupported types)
- Stored data is corrupted or incompatible with the target type
- A breaking change to a type's structure makes existing data unreadable

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `TargetType` | `Type?` | The type that failed to serialize or deserialize |
| `InnerException` | `Exception` | The underlying serializer exception |

### Example

```csharp
try
{
    // Stored as int, reading as string
    await store.SetAsync("count", 42);
    var result = await store.GetAsync<ComplexType>("count");
}
catch (StateSerializationException ex)
{
    Console.WriteLine($"Failed to deserialize as {ex.TargetType?.FullName}");
    Console.WriteLine($"Cause: {ex.InnerException?.Message}");
}
```

### Common Causes and Solutions

| Cause | Solution |
|-------|----------|
| Type mismatch (stored as `int`, reading as `string`) | Ensure `GetAsync<T>` uses the same `T` as `SetAsync<T>` |
| Circular reference | Configure `JsonSerializerOptions` with `ReferenceHandler.Preserve` or restructure the type |
| Missing parameterless constructor | Add a parameterless constructor or configure a custom converter |
| Renamed/removed properties | Use `[JsonPropertyName]` attributes for stable serialization names |
| Corrupted file data | Delete the corrupted entry and re-create it |

## StorageProviderException

Thrown when the storage backend fails. The exception wraps the underlying I/O error with context about the operation.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string?` | The key being operated on |
| `Operation` | `string?` | The operation that failed (`"Read"`, `"Write"`, `"Delete"`, `"Exists"`) |
| `ProviderType` | `Type?` | The type of the storage provider |
| `InnerException` | `Exception` | The underlying storage error |

### Example

```csharp
try
{
    await store.SetAsync("key", "value");
}
catch (StorageProviderException ex)
{
    Console.WriteLine($"Operation '{ex.Operation}' failed for key '{ex.Key}'");
    Console.WriteLine($"Provider: {ex.ProviderType?.Name}");
    Console.WriteLine($"Cause: {ex.InnerException?.Message}");
}
```

### Common Causes and Solutions

| Cause | Solution |
|-------|----------|
| Directory does not exist | Ensure `BasePath` is writable; `FileSystemStorageProvider` creates it at construction |
| Permission denied | Check file system permissions for the configured `BasePath` |
| Disk full | Free disk space or change `BasePath` to a different volume |
| File locked by another process | Ensure no external tools are locking state files |
| Network drive unavailable | Use local storage or implement retry logic |

### Automatic Wrapping

The core `StateStoreImplementation` catches all exceptions from the storage layer (except `StateStoreException` and `OperationCanceledException`) and wraps them in `StorageProviderException`:

```csharp
// Internal behavior:
catch (Exception ex) when (ex is not StateStoreException and not OperationCanceledException)
{
    throw new StorageProviderException(
        $"Failed to write state for key '{key}'.",
        key, "Write", pipeline.GetType(), ex);
}
```

This means you always get structured context, even when the underlying error is a raw `IOException` or `UnauthorizedAccessException`.

## StateStoreConcurrencyException

Thrown for lock-related failures. Currently reserved for future use (e.g., lock acquisition timeouts).

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string?` | The key that failed to lock |

In the current implementation, lock acquisition respects the `CancellationToken` and throws `OperationCanceledException` when cancelled. `StateStoreConcurrencyException` will be used if explicit lock timeout policies are introduced in a future release.

## MiddlewareException

Thrown when a middleware component throws an unhandled exception (one that is not a `StateStoreException` or `OperationCanceledException`).

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `MiddlewareType` | `Type?` | The type of the middleware that threw |
| `InnerException` | `Exception` | The original exception from the middleware |

### Example

```csharp
try
{
    await store.GetAsync<string>("key");
}
catch (MiddlewareException ex)
{
    Console.WriteLine($"Middleware {ex.MiddlewareType?.Name} failed");
    Console.WriteLine($"Cause: {ex.InnerException?.Message}");
}
```

## OperationCanceledException

This is a standard .NET exception, not a StateStore-specific one. It is thrown when:

- The `CancellationToken` passed to a method is cancelled
- Lock acquisition is cancelled
- A storage provider respects cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

try
{
    await store.GetAsync<string>("key", cts.Token);
}
catch (OperationCanceledException)
{
    // Token was cancelled or timed out
}
```

`OperationCanceledException` is never wrapped in a `StateStoreException`. It propagates directly to the caller.

## ArgumentException

Thrown immediately when key validation fails. This is a guard check before any I/O occurs:

```csharp
try
{
    await store.GetAsync<string>(""); // empty key
}
catch (ArgumentException ex)
{
    // "Key must not be null, empty, or whitespace."
}
```

## Error Handling Patterns

### Catch-All for StateStore Errors

```csharp
try
{
    var result = await store.GetAsync<UserSettings>("user:settings");
    // use result
}
catch (StateStoreException ex)
{
    logger.LogError(ex, "Failed to access state store");
    // Fall back to defaults
    return UserSettings.Default;
}
```

### Granular Error Handling

```csharp
try
{
    await store.SetAsync("key", complexObject);
}
catch (StateSerializationException ex)
{
    logger.LogError(ex, "Cannot serialize {Type}", ex.TargetType?.Name);
    throw; // Re-throw — caller needs to fix the type
}
catch (StorageProviderException ex)
{
    logger.LogWarning(ex, "Storage temporarily unavailable for key '{Key}'", ex.Key);
    // Queue for retry
}
catch (MiddlewareException ex)
{
    logger.LogError(ex, "Middleware {Middleware} failed", ex.MiddlewareType?.Name);
    // Disable problematic middleware or re-throw
}
```

### Resilient Read with Fallback

```csharp
public async Task<AppSettings> GetSettingsAsync()
{
    try
    {
        var settings = await _store.GetAsync<AppSettings>("settings");
        if (settings is not null)
        {
            return settings;
        }
    }
    catch (StateSerializationException)
    {
        // Stored data is corrupted or incompatible — delete and recreate
        await _store.DeleteAsync("settings");
    }
    catch (StorageProviderException)
    {
        // Storage unavailable — use defaults this time
    }

    var defaults = AppSettings.CreateDefault();
    try
    {
        await _store.SetAsync("settings", defaults);
    }
    catch (StateStoreException)
    {
        // Best effort — proceed with in-memory defaults
    }

    return defaults;
}
```

## Auto-Save Error Resilience

Auto-save errors are handled internally and never crash the host:

- Failed flush operations are logged at `Error` level
- Failed keys are re-queued for the next flush cycle
- The auto-save service continues operating after errors

See: [Auto-Save](09-Auto-Save.md)

## Related Guides

- [Basic Usage](03-Basic-Usage.md) - Method semantics and behavior
- [Middleware](07-Middleware.md) - Middleware error handling
- [Auto-Save](09-Auto-Save.md) - Auto-save error resilience
- [Testing](13-Testing.md) - Testing error scenarios
