# Dependency Injection

StateStore integrates with `Microsoft.Extensions.DependencyInjection`, the standard DI container used by ASP.NET Core, worker services, and any application built on `Microsoft.Extensions.Hosting`.

## AddStateStore

The primary registration method is an extension on `IServiceCollection`:

```csharp
using StateStore.Extensions;

services.AddStateStore(options =>
{
    options.UseFileSystem(fs => fs.BasePath = "./state");
    options.UseJsonSerializer(json => json.WriteIndented = true);
    options.UseMiddleware<LoggingMiddleware>();
    options.UseAutoSave(auto =>
    {
        auto.AddPeriodic(TimeSpan.FromMinutes(5));
        auto.AddShutdown();
    });
});
```

### What Gets Registered

`AddStateStore` registers the following services:

| Service | Implementation | Lifetime | Description |
|---------|---------------|----------|-------------|
| `IStateStore` | `StateStoreImplementation` | Singleton | Core dictionary-style store |
| `ITypedStateStore<T>` | `TypedStateStore<T>` (open generic) | Singleton | Type-scoped store for any `T` |
| `IStateSerializer` | `JsonStateSerializer` | Singleton | JSON serialization |
| `IStorageProvider` | `FileSystemStorageProvider` or `InMemoryStorageProvider` | Singleton | Storage backend |
| `IDirtyKeyTracker` | `DirtyKeyTracker` | Singleton | Tracks modified keys |
| `MiddlewarePipeline` | Factory-built | Singleton | Middleware chain |
| `IStateStoreMiddleware` | Per-registration | Singleton | Each middleware component |
| `AutoSaveHostedService` | (if auto-save configured) | Hosted service | Auto-save lifecycle |
| `IEnumerable<IAutoSaveStrategy>` | (if auto-save configured) | Singleton | Auto-save strategies |

All core services are registered using `TryAddSingleton`, which means:
- You can override any service by registering your own implementation **before** calling `AddStateStore`
- Calling `AddStateStore` multiple times is safe (duplicate registrations are skipped)

## Minimal Configuration

Calling `AddStateStore()` with no options uses the defaults:

```csharp
services.AddStateStore();
```

This registers:
- `FileSystemStorageProvider` with default options (base path `./state`, extension `.json`)
- `JsonStateSerializer` with default options (camelCase, no indentation, skip nulls)
- No middleware
- No auto-save

## Configuration Options

### StateStoreOptions

The fluent API on `StateStoreOptions` provides the following methods:

```csharp
services.AddStateStore(options =>
{
    // Storage provider selection
    options.UseFileSystem(fs => { ... });   // File system provider
    options.UseInMemory();                   // In-memory provider

    // Serializer configuration
    options.UseJsonSerializer(json => { ... });

    // Middleware pipeline
    options.UseMiddleware<T>();              // Add by type
    options.UseMiddleware(pipeline => { ... }); // Builder pattern

    // Auto-save strategies
    options.UseAutoSave(auto => { ... });
});
```

### Storage Provider Selection

```csharp
// File system (default)
options.UseFileSystem(fs =>
{
    fs.BasePath = "/var/lib/myapp/state";
    fs.FileExtension = ".dat";
});

// In-memory
options.UseInMemory();
```

### Serializer Configuration

```csharp
options.UseJsonSerializer(json =>
{
    json.WriteIndented = true;
    json.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
```

### Middleware Registration

```csharp
// Individual registration
options.UseMiddleware<LoggingMiddleware>();
options.UseMiddleware<TimingMiddleware>();

// Builder pattern
options.UseMiddleware(pipeline =>
{
    pipeline.Add<LoggingMiddleware>();
    pipeline.Add<TimingMiddleware>();
});
```

Middleware types must implement `IStateStoreMiddleware` and be resolvable by the DI container (typically through constructor injection).

### Auto-Save Configuration

```csharp
options.UseAutoSave(auto =>
{
    auto.AddPeriodic(TimeSpan.FromMinutes(5));  // Timer-based
    auto.AddShutdown();                          // On application stop
});
```

## Injecting State Stores

### IStateStore (Dictionary-Style)

```csharp
public class MyService
{
    private readonly IStateStore _store;

    public MyService(IStateStore store)
    {
        _store = store;
    }

    public async Task DoWorkAsync()
    {
        await _store.SetAsync("key", "value");
        var result = await _store.GetAsync<string>("key");
    }
}
```

### ITypedStateStore<T> (Type-Scoped)

```csharp
public class PreferencesService
{
    private readonly ITypedStateStore<UserPreferences> _store;

    public PreferencesService(ITypedStateStore<UserPreferences> store)
    {
        _store = store;
    }

    public async Task<UserPreferences?> GetPreferencesAsync()
    {
        return await _store.GetAsync();
    }
}
```

The open generic registration means you can inject `ITypedStateStore<T>` for any type `T` without additional configuration.

## Overriding Default Registrations

Because `AddStateStore` uses `TryAddSingleton`, registering your implementation before `AddStateStore` takes precedence:

```csharp
// Register custom serializer FIRST
services.AddSingleton<IStateSerializer, MessagePackSerializer>();

// Then register StateStore — it won't override your serializer
services.AddStateStore(options =>
{
    options.UseFileSystem();
});
```

The same pattern works for storage providers:

```csharp
services.AddSingleton<IStorageProvider>(new SqliteStorageProvider("Data Source=state.db"));
services.AddStateStore(); // FileSystemStorageProvider is NOT registered
```

## ASP.NET Core Integration

In an ASP.NET Core application:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStateStore(options =>
{
    options.UseFileSystem(fs => fs.BasePath = "./app-state");
    options.UseJsonSerializer();
    options.UseMiddleware<LoggingMiddleware>();
    options.UseAutoSave(auto =>
    {
        auto.AddPeriodic(TimeSpan.FromMinutes(5));
        auto.AddShutdown();
    });
});

var app = builder.Build();

app.MapGet("/settings", async (IStateStore store) =>
{
    var settings = await store.GetAsync<AppSettings>("app-settings");
    return Results.Ok(settings);
});

app.MapPost("/settings", async (IStateStore store, AppSettings settings) =>
{
    await store.SetAsync("app-settings", settings);
    return Results.NoContent();
});

app.Run();
```

## Worker Service Integration

In a background worker service:

```csharp
var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices(services =>
{
    services.AddStateStore(options =>
    {
        options.UseFileSystem(fs => fs.BasePath = "./worker-state");
        options.UseAutoSave(auto =>
        {
            auto.AddPeriodic(TimeSpan.FromMinutes(1));
            auto.AddShutdown();
        });
    });

    services.AddHostedService<ProcessingWorker>();
});

var host = builder.Build();
await host.RunAsync();
```

```csharp
public class ProcessingWorker : BackgroundService
{
    private readonly IStateStore _store;

    public ProcessingWorker(IStateStore store)
    {
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _store.UpsertAsync("processed-count", 1, x => x + 1, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

## AOT and Trimming Annotations

`AddStateStore` is annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` because it uses reflection-based JSON serialization and middleware type resolution. If you are publishing an AOT-compiled application, you will see trim warnings. To resolve these:

1. Register a custom AOT-compatible `IStateSerializer` (see [Serialization](06-Serialization.md))
2. Register middleware instances directly rather than by type

## Related Guides

- [Getting Started](01-Getting-Started.md) - Quick start with DI
- [Standalone Usage](11-Standalone-Usage.md) - Using StateStore without DI
- [Middleware](07-Middleware.md) - Configuring the middleware pipeline
- [Auto-Save](09-Auto-Save.md) - Auto-save strategy details
- [Serialization](06-Serialization.md) - Serializer configuration
- [Storage Providers](05-Storage-Providers.md) - Provider configuration
