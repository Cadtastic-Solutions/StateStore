using StateStore.Abstractions;
using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class StateStoreTests
{
    private readonly IStateStore _store;
    private readonly InMemoryStorageProvider _provider;

    public StateStoreTests()
    {
        _provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], _provider);
        _store = new StateStoreImplementation(serializer, pipeline);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenKeyDoesNotExist_Async()
    {
        var result = await _store.GetAsync<string>("nonexistent", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_StoresValue_ThenGetAsyncReturnsIt_Async()
    {
        await _store.SetAsync("key1", "hello", TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<string>("key1", TestContext.Current.CancellationToken);
        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingValue_Async()
    {
        await _store.SetAsync("key1", "first", TestContext.Current.CancellationToken);
        await _store.SetAsync("key1", "second", TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<string>("key1", TestContext.Current.CancellationToken);
        Assert.Equal("second", result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry_Async()
    {
        await _store.SetAsync("key1", "value", TestContext.Current.CancellationToken);
        await _store.DeleteAsync("key1", TestContext.Current.CancellationToken);
        var exists = await _store.ExistsAsync("key1", TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp_WhenKeyDoesNotExist_Async()
    {
        // Should not throw.
        await _store.DeleteAsync("nonexistent", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists_Async()
    {
        await _store.SetAsync("key1", 42, TestContext.Current.CancellationToken);
        var exists = await _store.ExistsAsync("key1", TestContext.Current.CancellationToken);
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyDoesNotExist_Async()
    {
        var exists = await _store.ExistsAsync("nonexistent", TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task UpsertAsync_InsertsValue_WhenKeyDoesNotExist_Async()
    {
        await _store.UpsertAsync("key1", "inserted", existing => existing + "_updated", TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<string>("key1", TestContext.Current.CancellationToken);
        Assert.Equal("inserted", result);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesValue_WhenKeyExists_Async()
    {
        await _store.SetAsync("counter", 10, TestContext.Current.CancellationToken);
        await _store.UpsertAsync("counter", 0, existing => existing + 1, TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<int>("counter", TestContext.Current.CancellationToken);
        Assert.Equal(11, result);
    }

    [Fact]
    public async Task UpsertAsync_UsesInsertValue_WhenKeyDoesNotExist_NotUpdateFactory_Async()
    {
        var factoryWasCalled = false;
        await _store.UpsertAsync("key1", 42, _ =>
        {
            factoryWasCalled = true;
            return 99;
        }, TestContext.Current.CancellationToken);

        Assert.False(factoryWasCalled);
        var result = await _store.GetAsync<int>("key1", TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SetAsync_WorksWithComplexTypes_Async()
    {
        var obj = new TestState { Name = "test", Count = 5 };
        await _store.SetAsync("complex", obj, TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<TestState>("complex", TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetAsync_ThrowsArgumentNullException_ForNullKey_Async()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.GetAsync<string>(null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task GetAsync_ThrowsArgumentException_ForEmptyKey_Async()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync<string>("", TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task GetAsync_ThrowsArgumentException_ForWhitespaceKey_Async()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync<string>("   ", TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SetAsync_ThrowsArgumentNullException_ForNullKey_Async()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SetAsync(null!, "value", TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task UpsertAsync_ThrowsArgumentNullException_ForNullFactory_Async()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.UpsertAsync<string>("key", "value", null!, TestContext.Current.CancellationToken).AsTask());
    }

    public sealed class TestState
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }
}
