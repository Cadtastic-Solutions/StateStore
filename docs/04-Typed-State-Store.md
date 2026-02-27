# Typed State Store

The typed state store provides a scoped, type-safe alternative to the dictionary-style `IStateStore`. Instead of managing string keys manually, `ITypedStateStore<TState>` derives the key automatically from the type, reducing boilerplate and eliminating key collisions.

## When to Use Typed vs Untyped

| Scenario | Recommended API |
|----------|----------------|
| Component owns one piece of state | `ITypedStateStore<TState>` |
| Multiple keys of the same type | `IStateStore` |
| Dynamic or user-defined keys | `IStateStore` |
| Key-value cache patterns | `IStateStore` |
| Configuration or preferences per component | `ITypedStateStore<TState>` |

## The ITypedStateStore Interface

```csharp
public interface ITypedStateStore<TState>
{
    ValueTask<TState?> GetAsync(CancellationToken cancellationToken = default);
    ValueTask SetAsync(TState value, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync(TState insertValue, Func<TState, TState> updateFactory, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default);
}
```

Notice there are no `key` parameters. The key is derived from `typeof(TState).FullName` at construction time.

## How Key Derivation Works

Internally, `TypedStateStore<TState>` is a thin wrapper around `IStateStore`. When you inject `ITypedStateStore<AppSettings>`, the library derives the key as:

```
"YourNamespace.AppSettings"
```

This means:
- Two injections of `ITypedStateStore<AppSettings>` in different services share the same underlying data (they use the same key).
- Two different types always map to different keys, even if they have the same structure.
- The key is stable across application restarts as long as the type's full name doesn't change.

## Registration

### With Dependency Injection

The typed state store is registered automatically as an open generic when you call `AddStateStore`:

```csharp
services.AddStateStore(options =>
{
    options.UseFileSystem();
    options.UseJsonSerializer();
});
```

You can then inject any `ITypedStateStore<T>` without additional registration:

```csharp
public class AnalyticsService
{
    private readonly ITypedStateStore<AnalyticsState> _store;

    public AnalyticsService(ITypedStateStore<AnalyticsState> store)
    {
        _store = store;
    }
}
```

### Without Dependency Injection

Use the builder's generic `Build<TState>()` method:

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build<UserPreferences>();

await store.SetAsync(new UserPreferences { Theme = "dark" });
```

## Complete Example

Here is a realistic example of a feature flag service that persists its state:

```csharp
public sealed class FeatureFlags
{
    public Dictionary<string, bool> Flags { get; set; } = new();
}

public sealed class FeatureFlagService
{
    private readonly ITypedStateStore<FeatureFlags> _store;

    public FeatureFlagService(ITypedStateStore<FeatureFlags> store)
    {
        _store = store;
    }

    public async Task<bool> IsEnabledAsync(string flagName)
    {
        var state = await _store.GetAsync();
        return state?.Flags.GetValueOrDefault(flagName, false) ?? false;
    }

    public async Task SetFlagAsync(string flagName, bool enabled)
    {
        await _store.UpsertAsync(
            new FeatureFlags { Flags = new() { [flagName] = enabled } },
            existing =>
            {
                existing.Flags[flagName] = enabled;
                return existing;
            });
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync()
    {
        var state = await _store.GetAsync();
        return state?.Flags ?? new Dictionary<string, bool>();
    }

    public async Task ResetAsync()
    {
        await _store.DeleteAsync();
    }
}
```

## Multiple Typed Stores in One Service

A service can depend on multiple typed stores for different aspects of its state:

```csharp
public class GameService
{
    private readonly ITypedStateStore<PlayerProfile> _profileStore;
    private readonly ITypedStateStore<GameProgress> _progressStore;
    private readonly ITypedStateStore<Leaderboard> _leaderboardStore;

    public GameService(
        ITypedStateStore<PlayerProfile> profileStore,
        ITypedStateStore<GameProgress> progressStore,
        ITypedStateStore<Leaderboard> leaderboardStore)
    {
        _profileStore = profileStore;
        _progressStore = progressStore;
        _leaderboardStore = leaderboardStore;
    }

    // Each store manages its own independent key and data
}
```

## UpsertAsync with Records

C# records work well with the typed store because the `with` expression creates clean update patterns:

```csharp
public record AppState
{
    public int LaunchCount { get; init; }
    public DateTime LastLaunched { get; init; }
    public bool OnboardingComplete { get; init; }
}

// Increment launch count and update timestamp
await store.UpsertAsync(
    new AppState { LaunchCount = 1, LastLaunched = DateTime.UtcNow },
    existing => existing with
    {
        LaunchCount = existing.LaunchCount + 1,
        LastLaunched = DateTime.UtcNow
    });
```

## Relationship to IStateStore

`ITypedStateStore<TState>` delegates every call to the underlying `IStateStore` using the derived key. This means:

- The same middleware pipeline, serializer, and storage provider apply
- Concurrency guarantees are identical
- Data stored via `ITypedStateStore<MyType>` can be read via `IStateStore.GetAsync<MyType>("MyNamespace.MyType")` and vice versa
- The `StoredState<T>` envelope is the same

This is important to understand when debugging: the typed store is a convenience wrapper, not a separate storage mechanism.

## Related Guides

- [Basic Usage](03-Basic-Usage.md) - The underlying `IStateStore` API
- [Dependency Injection](10-Dependency-Injection.md) - How typed stores are registered
- [Standalone Usage](11-Standalone-Usage.md) - Using `Build<TState>()` without DI
- [Testing](13-Testing.md) - Testing services that depend on typed stores
