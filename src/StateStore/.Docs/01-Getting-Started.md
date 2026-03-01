# Getting Started

This guide walks you through installing StateStore and writing your first state persistence code. By the end, you will have a working application that stores and retrieves typed data using both the dependency injection and standalone approaches.

## Prerequisites

- .NET 8 SDK or later (StateStore currently targets`net8.0`, `net9.0` and `net10.0`)
- A C# project (console app, ASP.NET Core, worker service, or class library)

## Installation

Add the StateStore NuGet package to your project:

```shell
dotnet add package StateStore
```

## Quick Start with Dependency Injection

The fastest path for hosted applications (ASP.NET Core, worker services, or anything using `Microsoft.Extensions.Hosting`):

```csharp
using StateStore.Extensions;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices(services =>
{
    services.AddStateStore(options =>
    {
        options.UseFileSystem(fs => fs.BasePath = "./app-state");
        options.UseJsonSerializer();
    });
});

var host = builder.Build();
```

Then inject `IStateStore` anywhere in your application:

```csharp
using StateStore.Abstractions;

public class UserPreferencesService
{
    private readonly IStateStore _store;

    public UserPreferencesService(IStateStore store)
    {
        _store = store;
    }

    public async Task SaveThemeAsync(string theme)
    {
        await _store.SetAsync("user:theme", theme);
    }

    public async Task<string?> LoadThemeAsync()
    {
        return await _store.GetAsync<string>("user:theme");
    }
}
```

## Quick Start without Dependency Injection

For console applications, scripts, or libraries that don't use a DI container, use the `StateStoreBuilder`:

```csharp
using StateStore;
using StateStore.Abstractions;

var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build();

// Store a value
await store.SetAsync("counter", 0);

// Read it back
var counter = await store.GetAsync<int>("counter");
Console.WriteLine($"Counter: {counter}"); // Counter: 0

// Update atomically
await store.UpsertAsync("counter", 1, existing => existing + 1);
counter = await store.GetAsync<int>("counter");
Console.WriteLine($"Counter: {counter}"); // Counter: 1
```

## Storing Complex Types

StateStore serializes any type that `System.Text.Json` can handle:

```csharp
public class AppSettings
{
    public string Theme { get; set; } = "light";
    public int FontSize { get; set; } = 14;
    public List<string> RecentFiles { get; set; } = [];
}

// Store
await store.SetAsync("settings", new AppSettings
{
    Theme = "dark",
    FontSize = 16,
    RecentFiles = ["/path/to/file1.txt", "/path/to/file2.txt"]
});

// Retrieve
var settings = await store.GetAsync<AppSettings>("settings");
Console.WriteLine(settings?.Theme); // dark
```

## Typed State Store

When your component owns a single piece of state, the typed store provides a cleaner API by removing the need for string keys:

```csharp
using StateStore.Abstractions;

public class DashboardService
{
    private readonly ITypedStateStore<DashboardState> _store;

    public DashboardService(ITypedStateStore<DashboardState> store)
    {
        _store = store;
    }

    public async Task UpdateVisitCountAsync()
    {
        await _store.UpsertAsync(
            new DashboardState { VisitCount = 1 },
            existing => existing with { VisitCount = existing.VisitCount + 1 });
    }
}

public record DashboardState
{
    public int VisitCount { get; init; }
}
```

## Project Structure

StateStore's architecture separates concerns into distinct layers:

```
StateStore/
  Abstractions/       Interfaces (IStateStore, IStorageProvider, etc.)
  Concurrency/        Per-key async locking
  Exceptions/         Typed exception hierarchy
  Extensions/         DI registration extensions
  Internal/           StoredState<T> metadata, key helpers, dirty tracking
  Middleware/          Pipeline and built-in LoggingMiddleware
  Options/            Configuration types
  Providers/
    FileSystem/       File-per-key persistent storage
    InMemory/         ConcurrentDictionary-backed ephemeral storage
  Serialization/      JSON serializer and options
  AutoSave/           Periodic and shutdown auto-save strategies
```

## What's Next

| Topic | Guide |
|-------|-------|
| Understand the layered architecture | [Core Concepts](02-Core-Concepts.md) |
| Learn the full IStateStore API | [Basic Usage](03-Basic-Usage.md) |
| Use typed, scoped state stores | [Typed State Store](04-Typed-State-Store.md) |
| Choose a storage backend | [Storage Providers](05-Storage-Providers.md) |
| Configure serialization | [Serialization](06-Serialization.md) |
| Add cross-cutting concerns | [Middleware](07-Middleware.md) |
| Understand thread safety | [Concurrency](08-Concurrency.md) |
| Enable automatic persistence | [Auto-Save](09-Auto-Save.md) |
| Set up with DI containers | [Dependency Injection](10-Dependency-Injection.md) |
| Use without a DI container | [Standalone Usage](11-Standalone-Usage.md) |
| Handle errors gracefully | [Error Handling](12-Error-Handling.md) |
| Write tests against StateStore | [Testing](13-Testing.md) |
| Build custom providers and middleware | [Extensibility](14-Extensibility.md) |
