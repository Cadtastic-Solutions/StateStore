using StateStore.Providers.FileSystem;

namespace StateStore.Tests;

public sealed class FileSystemStorageProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemStorageProvider _provider;

    public FileSystemStorageProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StateStoreTests_" + Guid.NewGuid().ToString("N"));
        _provider = new FileSystemStorageProvider(new FileSystemStorageOptions { BasePath = _tempDir });
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenFileDoesNotExist_Async()
    {
        var result = await _provider.ReadAsync("missing", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_ReturnsSameBytes_Async()
    {
        var data = "test data"u8.ToArray();
        await _provider.WriteAsync("key1", data, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_CreatesFile_Async()
    {
        await _provider.WriteAsync("key1", "data"u8.ToArray(), TestContext.Current.CancellationToken);
        Assert.True(await _provider.ExistsAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_Async()
    {
        await _provider.WriteAsync("key1", "data"u8.ToArray(), TestContext.Current.CancellationToken);
        await _provider.DeleteAsync("key1", TestContext.Current.CancellationToken);
        Assert.False(await _provider.ExistsAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp_WhenFileDoesNotExist_Async()
    {
        await _provider.DeleteAsync("missing", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile_Async()
    {
        await _provider.WriteAsync("key1", "first"u8.ToArray(), TestContext.Current.CancellationToken);
        var newData = "second"u8.ToArray();
        await _provider.WriteAsync("key1", newData, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal(newData, result);
    }

    [Fact]
    public async Task WriteAsync_HandlesSpecialCharactersInKey_Async()
    {
        var data = "data"u8.ToArray();
        await _provider.WriteAsync("path/with:special<chars>", data, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync("path/with:special<chars>", TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task WriteAsync_HandlesVeryLongKey_Async()
    {
        var longKey = new string('a', 300);
        var data = "data"u8.ToArray();
        await _provider.WriteAsync(longKey, data, TestContext.Current.CancellationToken);
        var result = await _provider.ReadAsync(longKey, TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenFileExists_Async()
    {
        await _provider.WriteAsync("key1", "data"u8.ToArray(), TestContext.Current.CancellationToken);
        Assert.True(await _provider.ExistsAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenFileDoesNotExist_Async()
    {
        Assert.False(await _provider.ExistsAsync("missing", TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
