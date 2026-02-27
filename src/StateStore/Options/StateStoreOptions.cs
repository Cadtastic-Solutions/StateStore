using StateStore.Abstractions;
using StateStore.Providers.FileSystem;
using StateStore.Serialization;

namespace StateStore.Options;

/// <summary>
/// Top-level configuration options for the StateStore library.
/// </summary>
public sealed class StateStoreOptions
{
    private readonly List<Type> _middlewareTypes = [];
    private readonly List<Action<IServiceProvider, AutoSaveOptionsBuilder>> _autoSaveConfigurations = [];

    /// <summary>
    /// Gets or sets the file system storage options. Null if not using file system provider.
    /// </summary>
    public FileSystemStorageOptions? FileSystem { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options.
    /// </summary>
    public JsonStateSerializerOptions Serializer { get; set; } = new();

    /// <summary>
    /// Gets or sets the storage provider type to use. Defaults to <see cref="StorageProviderKind.FileSystem"/>.
    /// </summary>
    public StorageProviderKind Provider { get; set; } = StorageProviderKind.FileSystem;

    /// <summary>
    /// Gets the registered middleware types in order.
    /// </summary>
    internal IReadOnlyList<Type> MiddlewareTypes => _middlewareTypes;

    /// <summary>
    /// Gets the registered auto-save configuration actions.
    /// </summary>
    internal IReadOnlyList<Action<IServiceProvider, AutoSaveOptionsBuilder>> AutoSaveConfigurations => _autoSaveConfigurations;

    /// <summary>
    /// Configures the file system storage provider.
    /// </summary>
    /// <param name="configure">An action to configure file system options.</param>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseFileSystem(Action<FileSystemStorageOptions>? configure = null)
    {
        Provider = StorageProviderKind.FileSystem;
        FileSystem ??= new FileSystemStorageOptions();
        configure?.Invoke(FileSystem);
        return this;
    }

    /// <summary>
    /// Configures the in-memory storage provider.
    /// </summary>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseInMemory()
    {
        Provider = StorageProviderKind.InMemory;
        return this;
    }

    /// <summary>
    /// Configures the JSON serializer.
    /// </summary>
    /// <param name="configure">An action to configure serializer options.</param>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseJsonSerializer(Action<JsonStateSerializerOptions>? configure = null)
    {
        configure?.Invoke(Serializer);
        return this;
    }

    /// <summary>
    /// Adds a middleware component to the pipeline.
    /// </summary>
    /// <typeparam name="T">The middleware type. Must implement <see cref="IStateStoreMiddleware"/>.</typeparam>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseMiddleware<T>() where T : IStateStoreMiddleware
    {
        _middlewareTypes.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Configures the middleware pipeline.
    /// </summary>
    /// <param name="configure">An action to configure the middleware pipeline.</param>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseMiddleware(Action<MiddlewarePipelineBuilder> configure)
    {
        var builder = new MiddlewarePipelineBuilder(this);
        configure(builder);
        return this;
    }

    /// <summary>
    /// Configures auto-save strategies.
    /// </summary>
    /// <param name="configure">An action to configure auto-save options.</param>
    /// <returns>This instance for chaining.</returns>
    public StateStoreOptions UseAutoSave(Action<AutoSaveOptionsBuilder> configure)
    {
        _autoSaveConfigurations.Add((_, builder) => configure(builder));
        return this;
    }
}

/// <summary>
/// Specifies the built-in storage provider to use.
/// </summary>
public enum StorageProviderKind
{
    /// <summary>
    /// File system-based storage provider.
    /// </summary>
    FileSystem,

    /// <summary>
    /// In-memory storage provider.
    /// </summary>
    InMemory
}

/// <summary>
/// Builder for configuring the middleware pipeline.
/// </summary>
public sealed class MiddlewarePipelineBuilder
{
    private readonly StateStoreOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewarePipelineBuilder"/>.
    /// </summary>
    /// <param name="options">The parent options instance.</param>
    internal MiddlewarePipelineBuilder(StateStoreOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Adds a middleware component to the pipeline.
    /// </summary>
    /// <typeparam name="T">The middleware type.</typeparam>
    /// <returns>This instance for chaining.</returns>
    public MiddlewarePipelineBuilder Add<T>() where T : IStateStoreMiddleware
    {
        _options.UseMiddleware<T>();
        return this;
    }
}

/// <summary>
/// Builder for configuring auto-save strategies.
/// </summary>
public sealed class AutoSaveOptionsBuilder
{
    private readonly List<Func<IServiceProvider, Abstractions.IAutoSaveStrategy>> _strategyFactories = [];

    /// <summary>
    /// Adds a periodic auto-save strategy.
    /// </summary>
    /// <param name="interval">The interval between flush cycles.</param>
    /// <returns>This instance for chaining.</returns>
    public AutoSaveOptionsBuilder AddPeriodic(TimeSpan interval)
    {
        _strategyFactories.Add(_ => new AutoSave.PeriodicAutoSaveStrategy(interval));
        return this;
    }

    /// <summary>
    /// Adds a shutdown auto-save strategy.
    /// </summary>
    /// <returns>This instance for chaining.</returns>
    public AutoSaveOptionsBuilder AddShutdown()
    {
        _strategyFactories.Add(sp =>
        {
            var lifetime = (Microsoft.Extensions.Hosting.IHostApplicationLifetime)sp.GetService(typeof(Microsoft.Extensions.Hosting.IHostApplicationLifetime))!;
            return new AutoSave.ShutdownAutoSaveStrategy(lifetime);
        });
        return this;
    }

    /// <summary>
    /// Builds all registered auto-save strategies.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <returns>The list of auto-save strategies.</returns>
    internal IReadOnlyList<Abstractions.IAutoSaveStrategy> Build(IServiceProvider serviceProvider)
    {
        return _strategyFactories.Select(f => f(serviceProvider)).ToList();
    }
}
