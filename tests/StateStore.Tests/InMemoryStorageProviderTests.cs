using StateStore.Providers.InMemory;

namespace StateStore.Tests;

public sealed class InMemoryStorageProviderTests
{
    private readonly InMemoryStorageProvider _provider = new();

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenKeyDoesNotExist_Async()
    {
        var result = await _provider.ReadAsync("missing", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_ReturnsSameBytes_Async()
    {
        var data = "hello"u8.ToArray();
        await _provider.WriteAsync("key1", data, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingData_Async()
    {
        await _provider.WriteAsync("key1", "first"u8.ToArray(), TestContext.Current.CancellationToken);
        var newData = "second"u8.ToArray();
        await _provider.WriteAsync("key1", newData, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal(newData, result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntry_Async()
    {
        await _provider.WriteAsync("key1", "data"u8.ToArray(), TestContext.Current.CancellationToken);
        await _provider.DeleteAsync("key1", TestContext.Current.CancellationToken);
        Assert.False(await _provider.ExistsAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp_WhenKeyDoesNotExist_Async()
    {
        await _provider.DeleteAsync("missing", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists_Async()
    {
        await _provider.WriteAsync("key1", "data"u8.ToArray(), TestContext.Current.CancellationToken);
        Assert.True(await _provider.ExistsAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyDoesNotExist_Async()
    {
        Assert.False(await _provider.ExistsAsync("missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllKeys_ReturnsAllStoredKeys_Async()
    {
        await _provider.WriteAsync("a", "1"u8.ToArray(), TestContext.Current.CancellationToken);
        await _provider.WriteAsync("b", "2"u8.ToArray(), TestContext.Current.CancellationToken);
        var keys = _provider.GetAllKeys();
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries_Async()
    {
        await _provider.WriteAsync("a", "1"u8.ToArray(), TestContext.Current.CancellationToken);
        await _provider.WriteAsync("b", "2"u8.ToArray(), TestContext.Current.CancellationToken);
        _provider.Clear();
        Assert.Empty(_provider.GetAllKeys());
    }

    [Fact]
    public async Task WriteAsync_RespectsCancellation_Async()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _provider.WriteAsync("key", "data"u8.ToArray(), cts.Token).AsTask());
    }

    [Fact]
    public async Task ReadAsync_RespectsCancellation_Async()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _provider.ReadAsync("key", cts.Token).AsTask());
    }
}
