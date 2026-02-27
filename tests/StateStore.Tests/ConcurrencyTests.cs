using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentWrites_ToSameKey_DoNotCorruptAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await store.SetAsync("counter", 0);

        var tasks = Enumerable.Range(0, 100).Select(_ => store.UpsertAsync("counter", 1, existing => existing + 1).AsTask());

        await Task.WhenAll(tasks);

        var result = await store.GetAsync<int>("counter");
        Assert.Equal(100, result);
    }

    [Fact]
    public async Task ConcurrentWrites_ToDifferentKeys_SucceedAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        var tasks = Enumerable.Range(0, 50).Select(i => store.SetAsync($"key_{i}", i).AsTask());

        await Task.WhenAll(tasks);

        for (var i = 0; i < 50; i++)
        {
            var result = await store.GetAsync<int>($"key_{i}");
            Assert.Equal(i, result);
        }
    }

    [Fact]
    public async Task ConcurrentReads_DoNotBlockAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await store.SetAsync("shared", "value");

        var tasks = Enumerable.Range(0, 100).Select(_ => store.GetAsync<string>("shared").AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("value", r));
    }

    [Fact]
    public async Task ConcurrentMixedOperations_AreConsistentAsync()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        var store = new StateStoreImplementation(serializer, pipeline);

        await store.SetAsync("key", 0);

        var writeTasks = Enumerable.Range(0, 50).Select(_ => store.UpsertAsync("key", 1, x => x + 1).AsTask());

        var readTasks = Enumerable.Range(0, 50).Select(_ =>
            store.GetAsync<int>("key").AsTask());

        await Task.WhenAll(writeTasks.Concat(readTasks));

        var final = await store.GetAsync<int>("key");
        Assert.Equal(50, final);
    }
}
