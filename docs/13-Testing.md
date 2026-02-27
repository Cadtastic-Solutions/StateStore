# Testing

StateStore is designed for testability. Every behavior is behind an interface, the in-memory provider serves as a ready-made test double, and the standalone builder creates fully wired stores without DI ceremony.

## Testing Philosophy

StateStore supports two testing strategies:

1. **Use the real StateStore with `InMemoryStorageProvider`** — Fast, no mocking needed, tests the full pipeline
2. **Mock `IStateStore` directly** — When you want to isolate your code from StateStore internals entirely

The first approach is recommended for most cases because it exercises the real serialization, middleware, and concurrency behavior.

## Using InMemoryStorageProvider

The in-memory provider is the primary test double. It behaves identically to `FileSystemStorageProvider` from the API perspective but stores data in memory with no I/O.

### Basic Test Setup

```csharp
using StateStore;
using StateStore.Abstractions;

public class MyServiceTests
{
    private readonly IStateStore _store;

    public MyServiceTests()
    {
        _store = new StateStoreBuilder()
            .UseInMemory()
            .UseJsonSerializer()
            .Build();
    }

    [Fact]
    public async Task SaveAndRetrieve_RoundTrips()
    {
        var service = new MyService(_store);

        await service.SavePreferencesAsync("dark");

        var result = await service.LoadPreferencesAsync();
        Assert.Equal("dark", result);
    }
}
```

### Test Isolation with Clear()

The in-memory provider exposes `GetAllKeys()` and `Clear()` for test assertions and cleanup:

```csharp
public class StateStoreTests : IDisposable
{
    private readonly InMemoryStorageProvider _provider;
    private readonly IStateStore _store;

    public StateStoreTests()
    {
        _provider = new InMemoryStorageProvider();
        _store = new StateStoreBuilder()
            .UseProvider(_provider)
            .UseJsonSerializer()
            .Build();
    }

    [Fact]
    public async Task Stores_AllExpectedKeys()
    {
        await _store.SetAsync("key1", "value1");
        await _store.SetAsync("key2", "value2");

        var keys = _provider.GetAllKeys();
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Equal(2, keys.Count);
    }

    public void Dispose()
    {
        _provider.Clear(); // Clean up between tests
    }
}
```

### Inspecting Raw Storage

For debugging, you can read raw bytes from the provider and deserialize manually:

```csharp
[Fact]
public async Task StoredData_ContainsMetadata()
{
    await _store.SetAsync("key", "hello");

    var rawBytes = await _provider.ReadAsync("key");
    Assert.NotNull(rawBytes);

    var json = System.Text.Encoding.UTF8.GetString(rawBytes);
    // json contains: {"value":"hello","createdAt":"...","updatedAt":"...","version":1}
    Assert.Contains("\"value\":\"hello\"", json);
    Assert.Contains("\"version\":1", json);
}
```

## Testing Typed State Stores

### Direct Construction

```csharp
[Fact]
public async Task TypedStore_ManagesState()
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build<AppSettings>();

    await store.SetAsync(new AppSettings { Theme = "dark" });

    var result = await store.GetAsync();
    Assert.NotNull(result);
    Assert.Equal("dark", result.Theme);
}
```

### Testing Services with Typed Store Dependencies

```csharp
public class FeatureFlagServiceTests
{
    private readonly FeatureFlagService _service;

    public FeatureFlagServiceTests()
    {
        var store = new StateStoreBuilder()
            .UseInMemory()
            .UseJsonSerializer()
            .Build<FeatureFlags>();

        _service = new FeatureFlagService(store);
    }

    [Fact]
    public async Task IsEnabled_ReturnsFalse_WhenFlagNotSet()
    {
        var result = await _service.IsEnabledAsync("new-feature");
        Assert.False(result);
    }

    [Fact]
    public async Task SetFlag_ThenIsEnabled_ReturnsTrue()
    {
        await _service.SetFlagAsync("new-feature", true);

        var result = await _service.IsEnabledAsync("new-feature");
        Assert.True(result);
    }
}
```

## Mocking IStateStore

When you want to completely isolate your service from StateStore behavior, mock the interface:

### With NSubstitute

```csharp
using NSubstitute;

[Fact]
public async Task Service_CallsSetAsync_WithCorrectKey()
{
    var mockStore = Substitute.For<IStateStore>();
    var service = new MyService(mockStore);

    await service.SaveThemeAsync("dark");

    await mockStore.Received(1)
        .SetAsync("user:theme", "dark", Arg.Any<CancellationToken>());
}

[Fact]
public async Task Service_ReturnsStoredValue()
{
    var mockStore = Substitute.For<IStateStore>();
    mockStore.GetAsync<string>("user:theme", Arg.Any<CancellationToken>())
        .Returns(new ValueTask<string?>("dark"));

    var service = new MyService(mockStore);

    var result = await service.LoadThemeAsync();
    Assert.Equal("dark", result);
}
```

### With Moq

```csharp
using Moq;

[Fact]
public async Task Service_ReturnsDefault_WhenKeyMissing()
{
    var mockStore = new Mock<IStateStore>();
    mockStore
        .Setup(s => s.GetAsync<UserSettings>(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((UserSettings?)null);

    var service = new PreferencesService(mockStore.Object);

    var result = await service.GetOrDefaultAsync();
    Assert.Equal(UserSettings.Default, result);
}
```

## Testing Middleware

### Testing a Middleware Component in Isolation

```csharp
[Fact]
public async Task TimingMiddleware_CallsNext()
{
    var logger = NullLogger<TimingMiddleware>.Instance;
    var middleware = new TimingMiddleware(logger);

    var nextCalled = false;
    var result = await middleware.OnReadAsync("key", () =>
    {
        nextCalled = true;
        return new ValueTask<byte[]?>("data"u8.ToArray());
    }, CancellationToken.None);

    Assert.True(nextCalled);
    Assert.NotNull(result);
}
```

### Testing Middleware in the Pipeline

```csharp
[Fact]
public async Task LoggingMiddleware_DoesNotAlterData()
{
    var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    var logger = loggerFactory.CreateLogger<LoggingMiddleware>();

    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .UseMiddleware(new LoggingMiddleware(logger))
        .Build();

    await store.SetAsync("key", "value");

    var result = await store.GetAsync<string>("key");
    Assert.Equal("value", result);
}
```

### Testing Short-Circuit Middleware

```csharp
public class CacheMiddlewareTests
{
    [Fact]
    public async Task CachedRead_DoesNotCallNext()
    {
        var middleware = new CacheMiddleware();
        var data = "cached"u8.ToArray();

        // Prime the cache via a write
        await middleware.OnWriteAsync("key", data, () => default, CancellationToken.None);

        // Read should return cached data without calling next
        var providerCalled = false;
        var result = await middleware.OnReadAsync("key", () =>
        {
            providerCalled = true;
            return new ValueTask<byte[]?>("provider-data"u8.ToArray());
        }, CancellationToken.None);

        Assert.False(providerCalled);
        Assert.Equal(data, result);
    }
}
```

## Concurrency Testing

StateStore guarantees per-key atomicity. Verify this in tests:

```csharp
[Fact]
public async Task ConcurrentUpserts_AreAtomic()
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build();

    await store.SetAsync("counter", 0);

    var tasks = Enumerable.Range(0, 100)
        .Select(_ => store.UpsertAsync("counter", 1, x => x + 1).AsTask());

    await Task.WhenAll(tasks);

    var result = await store.GetAsync<int>("counter");
    Assert.Equal(100, result);
}

[Fact]
public async Task ConcurrentWrites_ToDifferentKeys_AllSucceed()
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build();

    var tasks = Enumerable.Range(0, 50)
        .Select(i => store.SetAsync($"key_{i}", i).AsTask());

    await Task.WhenAll(tasks);

    for (var i = 0; i < 50; i++)
    {
        var result = await store.GetAsync<int>($"key_{i}");
        Assert.Equal(i, result);
    }
}
```

## Testing with FileSystemStorageProvider

When testing file system-specific behavior, use a temporary directory:

```csharp
public class FileSystemTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IStateStore _store;

    public FileSystemTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "StateStoreTests_" + Guid.NewGuid().ToString("N"));

        _store = new StateStoreBuilder()
            .UseFileSystem(fs => fs.BasePath = _tempDir)
            .UseJsonSerializer()
            .Build();
    }

    [Fact]
    public async Task Data_SurvivesRebuilding()
    {
        await _store.SetAsync("key", "persisted");

        // Build a new store pointing at the same directory
        var newStore = new StateStoreBuilder()
            .UseFileSystem(fs => fs.BasePath = _tempDir)
            .UseJsonSerializer()
            .Build();

        var result = await newStore.GetAsync<string>("key");
        Assert.Equal("persisted", result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
```

## Testing Error Scenarios

### Serialization Errors

```csharp
[Fact]
public async Task GetAsync_ThrowsSerializationException_OnTypeMismatch()
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build();

    // Store as a simple string
    await store.SetAsync("key", "plain text");

    // Try to read as a complex type — may fail depending on the shape
    // The behavior depends on whether JSON can coerce the stored shape
}
```

### Cancellation

```csharp
[Fact]
public async Task Operations_RespectCancellation()
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build();

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => store.SetAsync("key", "value", cts.Token).AsTask());
}
```

### Key Validation

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public async Task InvalidKeys_ThrowArgumentException(string? key)
{
    var store = new StateStoreBuilder()
        .UseInMemory()
        .UseJsonSerializer()
        .Build();

    await Assert.ThrowsAsync<ArgumentException>(
        () => store.GetAsync<string>(key!).AsTask());
}
```

## Test Organization Recommendations

```
tests/
  StateStore.Tests/
    Unit/
      InMemoryStorageProviderTests.cs     Provider unit tests
      JsonStateSerializerTests.cs         Serializer unit tests
      MiddlewarePipelineTests.cs          Middleware unit tests
    Integration/
      StateStoreTests.cs                  Full pipeline tests
      TypedStateStoreTests.cs             Typed store tests
      StateStoreBuilderTests.cs           Builder tests
      FileSystemStorageProviderTests.cs   File system tests
    Concurrency/
      ConcurrencyTests.cs                 Thread safety tests
```

## Related Guides

- [Storage Providers](05-Storage-Providers.md) - InMemoryStorageProvider details
- [Middleware](07-Middleware.md) - Testing middleware components
- [Error Handling](12-Error-Handling.md) - Exception types to test for
- [Standalone Usage](11-Standalone-Usage.md) - StateStoreBuilder for test setup
