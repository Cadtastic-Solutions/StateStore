# StateStore Documentation

**StateStore** is a lightweight, embeddable .NET library that provides a pluggable abstraction for persisting and restoring strongly-typed application state. It decouples *what* gets stored from *where* and *how* it gets stored.

## Target Frameworks

- .NET 8 (LTS)
- .NET 9

## Guides

### Getting Started

| # | Guide | Description |
|---|-------|-------------|
| 01 | [Getting Started](.Docs/01-Getting-Started.md) | Installation, quick start with DI and standalone, project structure |
| 02 | [Core Concepts](.Docs/02-Core-Concepts.md) | Architectural layers, design principles, the StoredState envelope |

### Using StateStore

| # | Guide | Description |
|---|-------|-------------|
| 03 | [Basic Usage](.Docs/03-Basic-Usage.md) | Full `IStateStore` API reference: Get, Set, Upsert, Delete, Exists |
| 04 | [Typed State Store](.Docs/04-Typed-State-Store.md) | Type-scoped `ITypedStateStore<T>` with automatic key derivation |
| 05 | [Storage Providers](.Docs/05-Storage-Providers.md) | InMemory and FileSystem providers, choosing a backend |
| 06 | [Serialization](.Docs/06-Serialization.md) | JSON serializer configuration, custom serializers, AOT considerations |
| 07 | [Middleware](.Docs/07-Middleware.md) | Pipeline model, built-in LoggingMiddleware, writing custom middleware |
| 08 | [Concurrency](.Docs/08-Concurrency.md) | Per-key reader-writer locks, UpsertAsync atomicity, thread safety |
| 09 | [Auto-Save](.Docs/09-Auto-Save.md) | Periodic and shutdown strategies, dirty key tracking |

### Integration

| # | Guide | Description |
|---|-------|-------------|
| 10 | [Dependency Injection](.Docs/10-Dependency-Injection.md) | `AddStateStore` registration, ASP.NET Core and worker service integration |
| 11 | [Standalone Usage](.Docs/11-Standalone-Usage.md) | `StateStoreBuilder` for DI-free scenarios |

### Reference

| # | Guide | Description |
|---|-------|-------------|
| 12 | [Error Handling](.Docs/12-Error-Handling.md) | Exception hierarchy, error patterns, resilient reads |
| 13 | [Testing](.Docs/13-Testing.md) | Using InMemoryStorageProvider, mocking, concurrency tests |
| 14 | [Extensibility](.Docs/14-Extensibility.md) | Custom providers, serializers, middleware, and auto-save strategies |

## Quick Reference

### Core Interfaces

| Interface | Purpose |
|-----------|---------|
| `IStateStore` | Dictionary-style state access with string keys |
| `ITypedStateStore<TState>` | Type-scoped state access with automatic key derivation |
| `IStorageProvider` | Raw byte I/O against a storage backend |
| `IStateSerializer` | Type-to-byte and byte-to-type conversion |
| `IStateStoreMiddleware` | Intercept and transform pipeline operations |
| `IAutoSaveStrategy` | Define triggers for automatic state persistence |

### Built-In Implementations

| Type | Description |
|------|-------------|
| `InMemoryStorageProvider` | `ConcurrentDictionary`-backed ephemeral storage |
| `FileSystemStorageProvider` | File-per-key durable storage with atomic writes |
| `JsonStateSerializer` | `System.Text.Json`-based serialization |
| `LoggingMiddleware` | Debug-level logging for all operations |
| `PeriodicAutoSaveStrategy` | Timer-based flush on configurable interval |
| `ShutdownAutoSaveStrategy` | Flush on `IHostApplicationLifetime.ApplicationStopping` |

### Configuration Entry Points

| Approach | Entry Point |
|----------|-------------|
| Dependency Injection | `services.AddStateStore(options => { ... })` |
| Standalone | `new StateStoreBuilder().UseInMemory().UseJsonSerializer().Build()` |

### Exception Hierarchy

```
StateStoreException
  ├── StateSerializationException     (TargetType)
  ├── StorageProviderException        (Key, Operation, ProviderType)
  ├── StateStoreConcurrencyException  (Key)
  └── MiddlewareException             (MiddlewareType)
```
