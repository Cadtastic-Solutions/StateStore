using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class EdgeCaseTests
{
    [Fact]
    public async Task SetAndGet_LargeObject_SucceedsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        var largeObject = new string('x', 100_000);
        await store.SetAsync("large", largeObject, TestContext.Current.CancellationToken);
        var result = await store.GetAsync<string>("large", TestContext.Current.CancellationToken);
        Assert.Equal(largeObject, result);
    }

    [Fact]
    public async Task SetAndGet_EmptyString_SucceedsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await store.SetAsync("empty", "", TestContext.Current.CancellationToken);
        var result = await store.GetAsync<string>("empty", TestContext.Current.CancellationToken);
        Assert.Equal("", result);
    }

    [Fact]
    public async Task SetAndGet_EmptyArray_SucceedsAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        var emptyArray = Array.Empty<int>();
        await store.SetAsync("emptyArray", emptyArray, TestContext.Current.CancellationToken);
        var result = await store.GetAsync<int[]>("emptyArray", TestContext.Current.CancellationToken) ?? [];
        Assert.Empty(result);
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsDefault_Async()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        var result = await store.GetAsync<int>("does_not_exist", TestContext.Current.CancellationToken);

        Assert.Equal(0, result);
    }
}
