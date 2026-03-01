using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class NullReferenceTests
{
    [Fact]
    public async Task SetAsync_NullKey_ThrowsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SetAsync(null!, 123, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_NullKey_ThrowsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.GetAsync<int>(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_NullKey_ThrowsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.UpsertAsync<int>("", 1, x => x + 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_NullUpdater_ThrowsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.UpsertAsync("key", 1, null!, TestContext.Current.CancellationToken));
    }
}
