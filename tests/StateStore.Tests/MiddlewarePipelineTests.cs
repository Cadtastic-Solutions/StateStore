using StateStore.Abstractions;
using StateStore.Middleware;
using StateStore.Providers.InMemory;

namespace StateStore.Tests;

public sealed class MiddlewarePipelineTests
{
    [Fact]
    public async Task ReadAsync_InvokesMiddlewareInOrder_Async()
    {
        var order = new List<string>();
        var provider = new InMemoryStorageProvider();
        await provider.WriteAsync("key1", "data"u8.ToArray());

        var middleware1 = new TrackingMiddleware("M1", order);
        var middleware2 = new TrackingMiddleware("M2", order);
        var pipeline = new MiddlewarePipeline([middleware1, middleware2], provider);

        await pipeline.ReadAsync("key1", CancellationToken.None);

        Assert.Equal(["M1:BeforeRead", "M2:BeforeRead", "M2:AfterRead", "M1:AfterRead"], order);
    }

    [Fact]
    public async Task WriteAsync_InvokesMiddlewareInOrder_Async()
    {
        var order = new List<string>();
        var provider = new InMemoryStorageProvider();

        var middleware1 = new TrackingMiddleware("M1", order);
        var middleware2 = new TrackingMiddleware("M2", order);
        var pipeline = new MiddlewarePipeline([middleware1, middleware2], provider);

        await pipeline.WriteAsync("key1", "data"u8.ToArray(), CancellationToken.None);

        Assert.Equal(["M1:BeforeWrite", "M2:BeforeWrite", "M2:AfterWrite", "M1:AfterWrite"], order);
    }

    [Fact]
    public async Task DeleteAsync_InvokesMiddlewareInOrderAsync()
    {
        var order = new List<string>();
        var provider = new InMemoryStorageProvider();

        var middleware1 = new TrackingMiddleware("M1", order);
        var pipeline = new MiddlewarePipeline([middleware1], provider);

        await pipeline.DeleteAsync("key1", CancellationToken.None);

        Assert.Equal(["M1:BeforeDelete", "M1:AfterDelete"], order);
    }

    [Fact]
    public async Task ReadAsync_MiddlewareCanShortCircuitAsync()
    {
        var provider = new InMemoryStorageProvider();
        var fakeData = "intercepted"u8.ToArray();
        var shortCircuitMiddleware = new ShortCircuitReadMiddleware(fakeData);
        var pipeline = new MiddlewarePipeline([shortCircuitMiddleware], provider);

        var result = await pipeline.ReadAsync("key1", CancellationToken.None);

        Assert.Equal(fakeData, result);
        Assert.False(await provider.ExistsAsync("key1"));
    }

    [Fact]
    public async Task ExistsAsync_BypassesMiddlewareAsync()
    {
        var order = new List<string>();
        var provider = new InMemoryStorageProvider();
        await provider.WriteAsync("key1", "data"u8.ToArray());

        var middleware = new TrackingMiddleware("M1", order);
        var pipeline = new MiddlewarePipeline([middleware], provider);

        var exists = await pipeline.ExistsAsync("key1", CancellationToken.None);

        Assert.True(exists);
        Assert.Empty(order);
    }

    private sealed class TrackingMiddleware : IStateStoreMiddleware
    {
        private readonly string _name;
        private readonly List<string> _order;

        public TrackingMiddleware(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public async ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
        {
            _order.Add($"{_name}:BeforeRead");
            var result = await next();
            _order.Add($"{_name}:AfterRead");
            return result;
        }

        public async ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken)
        {
            _order.Add($"{_name}:BeforeWrite");
            await next();
            _order.Add($"{_name}:AfterWrite");
        }

        public async ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken)
        {
            _order.Add($"{_name}:BeforeDelete");
            await next();
            _order.Add($"{_name}:AfterDelete");
        }
    }

    private sealed class ShortCircuitReadMiddleware : IStateStoreMiddleware
    {
        private readonly byte[] _data;

        public ShortCircuitReadMiddleware(byte[] data)
        {
            _data = data;
        }

        public ValueTask<byte[]?> OnReadAsync(string key, Func<ValueTask<byte[]?>> next, CancellationToken cancellationToken)
        {
            return new ValueTask<byte[]?>(_data);
        }

        public ValueTask OnWriteAsync(string key, ReadOnlyMemory<byte> data, Func<ValueTask> next, CancellationToken cancellationToken) => next();
        public ValueTask OnDeleteAsync(string key, Func<ValueTask> next, CancellationToken cancellationToken) => next();
    }
}
