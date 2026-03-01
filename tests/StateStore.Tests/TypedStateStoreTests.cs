using StateStore.Abstractions;
using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class TypedStateStoreTests
{
    private readonly ITypedStateStore<AppSettings> _typedStore;
    private readonly IStateStore _innerStore;

    public TypedStateStoreTests()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        _innerStore = new StateStoreImplementation(serializer, pipeline);
        _typedStore = new TypedStateStore<AppSettings>(_innerStore);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenNoStateExists_Async()
    {
        var result = await _typedStore.GetAsync(TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue_Async()
    {
        var settings = new AppSettings { Theme = "dark", FontSize = 14 };
        await _typedStore.SetAsync(settings, TestContext.Current.CancellationToken);
        var result = await _typedStore.GetAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("dark", result.Theme);
        Assert.Equal(14, result.FontSize);
    }

    [Fact]
    public async Task DeleteAsync_RemovesState_Async()
    {
        await _typedStore.SetAsync(new AppSettings { Theme = "light" }, TestContext.Current.CancellationToken);
        await _typedStore.DeleteAsync(TestContext.Current.CancellationToken);
        var exists = await _typedStore.ExistsAsync(TestContext.Current.CancellationToken);
        Assert.False(exists);
    }

    [Fact]
    public async Task UpsertAsync_InsertsWhenNoStateExists_Async()
    {
        var insertValue = new AppSettings { Theme = "dark", FontSize = 12 };
        await _typedStore.UpsertAsync(insertValue, existing => existing with { FontSize = 16 }, TestContext.Current.CancellationToken);
        var result = await _typedStore.GetAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("dark", result.Theme);
        Assert.Equal(12, result.FontSize);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesWhenStateExists_Async()
    {
        await _typedStore.SetAsync(new AppSettings { Theme = "dark", FontSize = 12 }, TestContext.Current.CancellationToken);
        await _typedStore.UpsertAsync(
            new AppSettings { Theme = "default" },
            existing => existing with { FontSize = existing.FontSize + 2 }, TestContext.Current.CancellationToken);
        var result = await _typedStore.GetAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("dark", result.Theme);
        Assert.Equal(14, result.FontSize);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenStateExists_Async()
    {
        await _typedStore.SetAsync(new AppSettings { Theme = "dark" }, TestContext.Current.CancellationToken);
        Assert.True(await _typedStore.ExistsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenNoStateExists_Async()
    {
        Assert.False(await _typedStore.ExistsAsync(TestContext.Current.CancellationToken));
    }

    public sealed record AppSettings
    {
        public string? Theme { get; init; }
        public int FontSize { get; init; }
    }
}
