namespace StateStore.Tests;

public sealed class StateStoreBuilderTests
{
    [Fact]
    public async Task Build_WithInMemory_CreatesWorkingStore_Async()
    {
        var store = new StateStoreBuilder()
            .UseInMemory()
            .UseJsonSerializer()
            .Build();

        await store.SetAsync("key", "value", TestContext.Current.CancellationToken);
        var result = await store.GetAsync<string>("key", TestContext.Current.CancellationToken);
        Assert.Equal("value", result);
    }

    [Fact]
    public async Task Build_WithDefaults_UsesInMemoryAndJson_Async()
    {
        var store = new StateStoreBuilder().Build();

        await store.SetAsync("key", 42, TestContext.Current.CancellationToken);
        var result = await store.GetAsync<int>("key", TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task BuildGeneric_CreatesTypedStore_Async()
    {
        var typedStore = new StateStoreBuilder()
            .UseInMemory()
            .UseJsonSerializer()
            .Build<TestConfig>();

        await typedStore.SetAsync(new TestConfig { Setting = "on" }, TestContext.Current.CancellationToken);
        var result = await typedStore.GetAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("on", result.Setting);
    }

    [Fact]
    public async Task Build_WithFileSystem_CreatesWorkingStore_Async()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "StateStoreBuilderTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStoreBuilder()
                .UseFileSystem(o => o.BasePath = tempDir)
                .UseJsonSerializer()
                .Build();

            await store.SetAsync("key", "persisted", TestContext.Current.CancellationToken);
            var result = await store.GetAsync<string>("key", TestContext.Current.CancellationToken);
            Assert.Equal("persisted", result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public sealed class TestConfig
    {
        public string? Setting { get; set; }
    }
}
