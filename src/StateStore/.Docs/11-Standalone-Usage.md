# Standalone Usage

StateStore can be used without a dependency injection container. The `StateStoreBuilder` provides a fluent API for constructing fully configured `IStateStore` and `ITypedStateStore<T>` instances manually.

## When to Use the Builder

- Console applications without `IServiceCollection`
- Class libraries that don't impose a DI framework on consumers
- Scripting and prototyping scenarios
- Unit tests where DI overhead is unnecessary

## StateStoreBuilder

```csharp
using StateStore;

var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build();
```

### Available Methods

| Method | Description |
|--------|-------------|
| `UseInMemory()` | Use the in-memory storage provider |
| `UseFileSystem(Action<FileSystemStorageOptions>?)` | Use the file system storage provider |
| `UseProvider(IStorageProvider)` | Use a custom storage provider instance |
| `UseJsonSerializer(Action<JsonStateSerializerOptions>?)` | Use the JSON serializer |
| `UseSerializer(IStateSerializer)` | Use a custom serializer instance |
| `UseMiddleware(IStateStoreMiddleware)` | Add a middleware instance to the pipeline |
| `Build()` | Create an `IStateStore` instance |
| `Build<TState>()` | Create an `ITypedStateStore<TState>` instance |

### Defaults

If you call `Build()` without configuring a provider or serializer, the builder uses:
- `InMemoryStorageProvider` as the default storage provider
- `JsonStateSerializer` with default settings as the serializer

```csharp
// Minimal: uses in-memory + JSON with defaults
var store = new StateStoreBuilder().Build();
```

## Building an IStateStore

### In-Memory Store

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build();

await store.SetAsync("counter", 0);
await store.UpsertAsync("counter", 1, x => x + 1);
var result = await store.GetAsync<int>("counter");
// result == 1
```

### File System Store

```csharp
var store = new StateStoreBuilder()
    .UseFileSystem(fs =>
    {
        fs.BasePath = "./my-app-state";
        fs.FileExtension = ".json";
    })
    .UseJsonSerializer(json =>
    {
        json.WriteIndented = true;
    })
    .Build();

await store.SetAsync("user-prefs", new UserPreferences { Theme = "dark" });
```

### With Custom Provider

```csharp
var provider = new SqliteStorageProvider("Data Source=state.db");

var store = new StateStoreBuilder()
    .UseProvider(provider)
    .UseJsonSerializer()
    .Build();
```

### With Custom Serializer

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseSerializer(new MessagePackSerializer())
    .Build();
```

## Building a Typed State Store

Use the generic `Build<TState>()` method to create a type-scoped store:

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build<AppSettings>();

await store.SetAsync(new AppSettings { Debug = true });
var settings = await store.GetAsync();
// settings.Debug == true
```

The typed store derives its key from `typeof(TState).FullName`, just like the DI-registered version.

## Adding Middleware

The standalone builder accepts middleware instances (not types, since there is no DI container to resolve them):

```csharp
var logger = LoggerFactory
    .Create(builder => builder.AddConsole())
    .CreateLogger<LoggingMiddleware>();

var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .UseMiddleware(new LoggingMiddleware(logger))
    .Build();
```

Multiple middleware components can be chained:

```csharp
var store = new StateStoreBuilder()
    .UseFileSystem(fs => fs.BasePath = "./state")
    .UseJsonSerializer()
    .UseMiddleware(new LoggingMiddleware(logger))
    .UseMiddleware(new CacheMiddleware())
    .UseMiddleware(new MaxSizeMiddleware(maxBytes: 1_048_576))
    .Build();
```

Middleware executes in registration order, the same as with the DI approach.

## Lifecycle Management

Unlike the DI-registered store, the standalone store does not participate in `IHostedService` lifecycle management. This means:

- **No auto-save:** The `AutoSaveHostedService` requires `IHostedService` infrastructure. For standalone usage, you must persist state explicitly.
- **No graceful shutdown flush:** There is no automatic shutdown handler. If you need a final flush, call it explicitly before the process exits.
- **Disposal:** The builder returns `IStateStore`, which does not implement `IDisposable`. The underlying storage provider and middleware instances are managed by the consumer.

### Manual Shutdown Pattern

```csharp
var store = new StateStoreBuilder()
    .UseFileSystem(fs => fs.BasePath = "./state")
    .UseJsonSerializer()
    .Build();

AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
{
    // Perform any final persistence if needed
    Console.WriteLine("Application shutting down, state already persisted by SetAsync/UpsertAsync");
};
```

## Complete Console Application Example

```csharp
using StateStore;
using StateStore.Abstractions;

// Build the store
var store = new StateStoreBuilder()
    .UseFileSystem(fs => fs.BasePath = "./todo-state")
    .UseJsonSerializer()
    .Build();

// Load existing state or start fresh
var todos = await store.GetAsync<List<string>>("todos") ?? [];

// Main loop
while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(input) || input == "quit")
        break;

    if (input.StartsWith("add "))
    {
        var item = input[4..];
        todos.Add(item);
        await store.SetAsync("todos", todos);
        Console.WriteLine($"Added: {item}");
    }
    else if (input == "list")
    {
        for (var i = 0; i < todos.Count; i++)
            Console.WriteLine($"  {i + 1}. {todos[i]}");
    }
    else if (input.StartsWith("remove ") && int.TryParse(input[7..], out var index))
    {
        if (index > 0 && index <= todos.Count)
        {
            var removed = todos[index - 1];
            todos.RemoveAt(index - 1);
            await store.SetAsync("todos", todos);
            Console.WriteLine($"Removed: {removed}");
        }
    }
    else if (input == "count")
    {
        Console.WriteLine($"Total: {todos.Count}");
    }
}

Console.WriteLine("Goodbye!");
```

## Comparison: DI vs Standalone

| Feature | DI (`AddStateStore`) | Standalone (`StateStoreBuilder`) |
|---------|---------------------|----------------------------------|
| Service lifetime management | Automatic (singleton) | Manual |
| Open generic `ITypedStateStore<T>` | Automatic for any `T` | Per-call via `Build<TState>()` |
| Auto-save strategies | Supported | Not available |
| Middleware resolution | By type (DI resolves) | By instance (manual creation) |
| Graceful shutdown hooks | Via `IHostedService` | Manual |
| Multiple named stores | Via keyed services | Multiple builder instances |

## Related Guides

- [Getting Started](01-Getting-Started.md) - Quick start with both approaches
- [Dependency Injection](10-Dependency-Injection.md) - The DI alternative
- [Storage Providers](05-Storage-Providers.md) - Provider options
- [Middleware](07-Middleware.md) - Middleware configuration
- [Serialization](06-Serialization.md) - Serializer options
