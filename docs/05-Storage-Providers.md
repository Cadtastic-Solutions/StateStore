# Storage Providers

A storage provider is the lowest layer in the StateStore architecture. It handles raw byte I/O against a physical storage mechanism. Providers have no knowledge of types, serialization, or application logic.

## The IStorageProvider Interface

```csharp
public interface IStorageProvider
{
    ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);
    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
```

The interface is intentionally minimal (four methods). This makes it straightforward to implement custom providers for any storage backend.

### Design Decisions

- **Byte-level operations:** Providers operate on `byte[]` and `ReadOnlyMemory<byte>`. This separates serialization concerns from storage concerns completely.
- **Null for missing keys:** `ReadAsync` returns `null` when a key does not exist rather than throwing an exception. This aligns with the "missing key" semantic used throughout the library.
- **No-op deletes:** `DeleteAsync` is a no-op when the key does not exist. It never throws for missing keys.
- **ValueTask returns:** All methods return `ValueTask` to avoid `Task` allocations when the operation completes synchronously (e.g., `InMemoryStorageProvider`).

## InMemoryStorageProvider

Backed by `ConcurrentDictionary<string, byte[]>`. Data is stored entirely in memory and is lost when the process exits.

### Characteristics

| Property | Value |
|----------|-------|
| Persistence | None (ephemeral) |
| Thread safety | Yes (`ConcurrentDictionary`) |
| Performance | O(1) reads and writes |
| Capacity | Limited by available memory |

### When to Use

- **Unit and integration tests** — No file system setup or cleanup needed
- **Development and prototyping** — Quick iteration without persistence
- **Ephemeral scenarios** — Cache-like state that doesn't survive restarts
- **Benchmarking** — Isolate StateStore overhead from I/O latency

### Configuration

```csharp
// With DI
services.AddStateStore(options =>
{
    options.UseInMemory();
});

// Standalone
var store = new StateStoreBuilder()
    .UseInMemory()
    .Build();
```

### Testing Helpers

The in-memory provider exposes additional methods for test assertions:

```csharp
var provider = new InMemoryStorageProvider();

// Write some data
await provider.WriteAsync("key1", "data1"u8.ToArray());
await provider.WriteAsync("key2", "data2"u8.ToArray());

// Inspect stored keys
ICollection<string> keys = provider.GetAllKeys();
// keys contains "key1", "key2"

// Reset between tests
provider.Clear();
// keys is now empty
```

## FileSystemStorageProvider

Persists each key as a separate file in a configurable directory. Designed for production use where state must survive process restarts.

### Characteristics

| Property | Value |
|----------|-------|
| Persistence | Durable (disk) |
| Thread safety | Yes (via StateStore's locking layer) |
| Atomic writes | Yes (write-to-temp-then-rename) |
| File naming | Direct key name or SHA256 hash for unsafe keys |
| Default directory | `./state` |
| Default extension | `.json` |

### Configuration

```csharp
// With DI
services.AddStateStore(options =>
{
    options.UseFileSystem(fs =>
    {
        fs.BasePath = "/var/lib/myapp/state";
        fs.FileExtension = ".dat";
    });
});

// Standalone
var store = new StateStoreBuilder()
    .UseFileSystem(fs =>
    {
        fs.BasePath = "./app-data";
        fs.FileExtension = ".json";
    })
    .Build();
```

### FileSystemStorageOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BasePath` | `string` | `./state` (relative to CWD) | Directory where state files are stored. Created automatically if it doesn't exist. |
| `FileExtension` | `string` | `.json` | File extension appended to each key file. |

### Atomic Write Strategy

The file system provider uses a write-to-temp-then-rename pattern to prevent data corruption:

1. Data is written to a temporary file (`{key}.tmp`) in the same directory
2. The temporary file is atomically moved to the target path using `File.Move(overwrite: true)`
3. If the write fails mid-operation, the temporary file is cleaned up and the original file remains intact

This guarantees that a state file is never partially written. On most file systems, `File.Move` within the same directory is an atomic metadata operation.

```
Write flow:
  data → key.json.tmp → File.Move → key.json
                                      ↑
                     Original file is replaced atomically
```

### File Naming

Keys that are valid file names (no invalid characters, 200 characters or fewer) are used directly as file names:

```
Key: "user-preferences"  →  File: user-preferences.json
Key: "app.config"        →  File: app.config.json
```

Keys containing invalid file name characters or exceeding 200 characters are hashed using SHA256:

```
Key: "path/to/something"  →  File: 7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069.json
Key: (201+ character key)  →  File: (sha256 hex).json
```

### Directory Structure

The provider creates a flat directory structure. All files live in the configured `BasePath`:

```
state/
  user-preferences.json
  app-config.json
  7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069.json
```

## Choosing a Provider

| Consideration | InMemory | FileSystem |
|---------------|----------|------------|
| Data survives restarts | No | Yes |
| I/O overhead | None | Disk I/O |
| Setup complexity | None | Directory permissions |
| Suitable for tests | Preferred | Possible (needs temp dir) |
| Suitable for production | Only for caches | Yes |
| Capacity limit | Process memory | Disk space |

## Writing a Custom Provider

Implementing `IStorageProvider` is straightforward. For example, a SQLite-backed provider:

```csharp
public sealed class SqliteStorageProvider : IStorageProvider
{
    private readonly string _connectionString;

    public SqliteStorageProvider(string connectionString)
    {
        _connectionString = connectionString;
        // Create table if not exists during construction
    }

    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        // SELECT data FROM state WHERE key = @key
    }

    public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        // INSERT OR REPLACE INTO state (key, data) VALUES (@key, @data)
    }

    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        // DELETE FROM state WHERE key = @key
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        // SELECT COUNT(1) FROM state WHERE key = @key
    }
}
```

Register custom providers via DI or the builder:

```csharp
// DI: Register before AddStateStore to override the default
services.AddSingleton<IStorageProvider>(new SqliteStorageProvider("Data Source=state.db"));
services.AddStateStore(); // Won't register FileSystemStorageProvider because TryAdd is used

// Builder
var store = new StateStoreBuilder()
    .UseProvider(new SqliteStorageProvider("Data Source=state.db"))
    .UseJsonSerializer()
    .Build();
```

See [Extensibility](14-Extensibility.md) for more details on implementing custom providers.

## Related Guides

- [Core Concepts](02-Core-Concepts.md) - Where providers fit in the architecture
- [Serialization](06-Serialization.md) - What gets serialized before reaching the provider
- [Error Handling](12-Error-Handling.md) - How provider errors are wrapped
- [Testing](13-Testing.md) - Using InMemoryStorageProvider as a test double
- [Extensibility](14-Extensibility.md) - Building custom storage providers
