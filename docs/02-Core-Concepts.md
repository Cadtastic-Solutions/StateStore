# Core Concepts

StateStore is built around a layered architecture where each layer has a single, well-defined responsibility. Understanding these layers is essential to using the library effectively and extending it with custom implementations.

## The Problem StateStore Solves

Applications frequently need to persist small pieces of state: user preferences, cached computations, configuration overrides, counters, session data. Without a dedicated abstraction, developers couple their domain logic directly to a specific storage mechanism (files, databases, registries), making the code harder to test, harder to port across environments, and harder to change later.

StateStore decouples **what** gets stored from **where** and **how** it gets stored. Your application code depends on `IStateStore`, and the storage backend, serialization format, and middleware pipeline are configured independently.

## Architectural Layers

```
  Application Code
        |
  IStateStore / ITypedStateStore<T>    (Public API)
        |
  Concurrency Layer                    (Per-key reader-writer locks)
        |
  Serialization Layer                  (IStateSerializer)
        |
  Middleware Pipeline                  (IStateStoreMiddleware chain)
        |
  Storage Provider                     (IStorageProvider)
        |
  Physical Storage                     (Files, Memory, Custom)
```

### Layer 1: Public API

The two entry points for consumers:

- **`IStateStore`** provides dictionary-style access with string keys. Use this when you manage multiple heterogeneous pieces of state or when keys are dynamic.
- **`ITypedStateStore<TState>`** provides scoped access for a single type. The key is derived automatically from the type name. Use this when a component owns exactly one piece of state.

Both interfaces expose the same five operations: `GetAsync`, `SetAsync`, `UpsertAsync`, `DeleteAsync`, and `ExistsAsync`. All methods are async, return `ValueTask` or `ValueTask<T>`, and accept a `CancellationToken`.

See: [Basic Usage](03-Basic-Usage.md), [Typed State Store](04-Typed-State-Store.md)

### Layer 2: Concurrency

StateStore uses per-key async reader-writer locks internally. Multiple readers can access the same key concurrently, but write operations acquire an exclusive lock per key. Operations on different keys never block each other.

The `UpsertAsync` operation holds the write lock for the entire read-modify-write cycle, guaranteeing atomicity without requiring the consumer to manage any synchronization.

See: [Concurrency](08-Concurrency.md)

### Layer 3: Serialization

Before data reaches the storage provider, it passes through an `IStateSerializer` that converts typed objects to raw bytes and back. The library ships with `JsonStateSerializer` backed by `System.Text.Json`, but the interface is simple enough to implement for any format (MessagePack, Protobuf, BSON, etc.).

Internally, StateStore wraps every value in a `StoredState<T>` envelope that carries metadata (`CreatedAt`, `UpdatedAt`, `Version`). This envelope is transparent to the consumer but enables future versioning and migration support without changing the storage format.

See: [Serialization](06-Serialization.md)

### Layer 4: Middleware Pipeline

Between the serializer and the storage provider sits a middleware pipeline modeled after ASP.NET Core's middleware pattern. Each middleware component can:

- **Inspect** operations (logging, metrics, auditing)
- **Transform** data (encryption, compression)
- **Short-circuit** operations (caching, validation)

Middleware components implement `IStateStoreMiddleware` and are invoked in registration order. Each component receives a `next` delegate to continue the pipeline or can return early to short-circuit.

See: [Middleware](07-Middleware.md)

### Layer 5: Storage Provider

At the bottom of the pipeline, an `IStorageProvider` performs raw byte I/O. Providers have no knowledge of types, serialization, or application logic. They implement four operations (`ReadAsync`, `WriteAsync`, `DeleteAsync`, `ExistsAsync`) that operate on `byte[]` and `ReadOnlyMemory<byte>`.

The library ships with two providers:

| Provider | Backing Store | Use Case |
|----------|--------------|----------|
| `FileSystemStorageProvider` | One file per key on disk | Production persistence |
| `InMemoryStorageProvider` | `ConcurrentDictionary` | Testing, ephemeral scenarios |

See: [Storage Providers](05-Storage-Providers.md)

## The StoredState Envelope

Every value persisted through StateStore is wrapped in an internal envelope:

```json
{
  "value": { "theme": "dark", "fontSize": 16 },
  "createdAt": "2026-01-15T10:30:00+00:00",
  "updatedAt": "2026-01-15T14:22:00+00:00",
  "version": 1
}
```

This envelope is managed automatically. When you call `SetAsync` for the first time, `CreatedAt` and `UpdatedAt` are both set to the current UTC time. On subsequent updates, only `UpdatedAt` changes, preserving the original creation timestamp.

The `Version` field is currently always `1` and is reserved for future schema migration support. When versioning is introduced in a future release, the existing storage format will remain compatible because the field is already present.

## Key Validation

All string keys passed to `IStateStore` must be non-null, non-empty, and not consist solely of whitespace. Violating this throws an `ArgumentException` immediately, before any I/O occurs.

```csharp
// These all throw ArgumentException:
await store.GetAsync<string>(null!);   // null
await store.GetAsync<string>("");      // empty
await store.GetAsync<string>("   ");   // whitespace
```

## ValueTask vs Task

All public API methods return `ValueTask` or `ValueTask<T>` instead of `Task`. This is a deliberate performance decision: when the underlying operation completes synchronously (as it does with `InMemoryStorageProvider`), `ValueTask` avoids allocating a `Task` object on the heap. For async completions, `ValueTask` wraps a `Task` internally with negligible overhead.

If you need to compose state store operations with `Task.WhenAll`, convert to `Task` first:

```csharp
await Task.WhenAll(
    store.SetAsync("a", 1).AsTask(),
    store.SetAsync("b", 2).AsTask(),
    store.SetAsync("c", 3).AsTask());
```

## ConfigureAwait(false)

StateStore is a library, not an application. All internal `await` calls use `ConfigureAwait(false)` to avoid capturing the synchronization context. This prevents deadlocks when the library is consumed from UI frameworks (WPF, WinForms, MAUI) or from ASP.NET classic.

## Design Principles

StateStore follows the SOLID principles throughout its architecture:

| Principle | Application |
|-----------|-------------|
| **Single Responsibility** | Each interface owns one concern: `IStateStore` (state operations), `IStorageProvider` (raw I/O), `IStateSerializer` (serialization), `IStateStoreMiddleware` (cross-cutting), `IAutoSaveStrategy` (persistence triggers). |
| **Open/Closed** | New backends, serializers, middleware, and auto-save strategies are added by implementing interfaces, never by modifying existing code. |
| **Liskov Substitution** | All `IStorageProvider` implementations are interchangeable. Swapping `FileSystem` for `InMemory` changes no consumer behavior. |
| **Interface Segregation** | `IStateStore` and `ITypedStateStore<T>` are separate interfaces. `IStorageProvider` has only 4 methods. Consumers depend only on what they use. |
| **Dependency Inversion** | Core logic depends on abstractions, never on concrete implementations. All wiring is done via DI or the builder. |

## Next Steps

- [Basic Usage](03-Basic-Usage.md) - Learn the full `IStateStore` API with detailed examples
- [Typed State Store](04-Typed-State-Store.md) - Explore the scoped, type-safe alternative
- [Dependency Injection](10-Dependency-Injection.md) - Configure StateStore in hosted applications
