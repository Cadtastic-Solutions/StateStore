using System.Diagnostics.CodeAnalysis;
using StateStore.Abstractions;
using StateStore.Internal;
using StateStore.Middleware;
using StateStore.Options;
using StateStore.Providers.FileSystem;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore;

/// <summary>
/// A standalone builder for creating <see cref="IStateStore"/> instances
/// without requiring a dependency injection container.
/// </summary>
[RequiresUnreferencedCode("StateStoreBuilder uses JsonStateSerializer which requires reflection-based JSON serialization.")]
[RequiresDynamicCode("StateStoreBuilder uses JsonStateSerializer which may require runtime code generation.")]
public sealed class StateStoreBuilder
{
    private IStorageProvider? _provider;
    private IStateSerializer? _serializer;
    private readonly List<IStateStoreMiddleware> _middlewares = [];

    /// <summary>
    /// Configures the builder to use the in-memory storage provider.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseInMemory()
    {
        _provider = new InMemoryStorageProvider();
        return this;
    }

    /// <summary>
    /// Configures the builder to use the file system storage provider.
    /// </summary>
    /// <param name="configure">Optional action to configure file system options.</param>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseFileSystem(Action<FileSystemStorageOptions>? configure = null)
    {
        var options = new FileSystemStorageOptions();
        configure?.Invoke(options);
        _provider = new FileSystemStorageProvider(options);
        return this;
    }

    /// <summary>
    /// Configures the builder to use a custom storage provider.
    /// </summary>
    /// <param name="provider">The storage provider instance.</param>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseProvider(IStorageProvider provider)
    {
        _provider = provider;
        return this;
    }

    /// <summary>
    /// Configures the builder to use the JSON serializer with default settings.
    /// </summary>
    /// <param name="configure">Optional action to configure serializer options.</param>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseJsonSerializer(Action<JsonStateSerializerOptions>? configure = null)
    {
        var options = new JsonStateSerializerOptions();
        configure?.Invoke(options);
        _serializer = new JsonStateSerializer(options);
        return this;
    }

    /// <summary>
    /// Configures the builder to use a custom serializer.
    /// </summary>
    /// <param name="serializer">The serializer instance.</param>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseSerializer(IStateSerializer serializer)
    {
        _serializer = serializer;
        return this;
    }

    /// <summary>
    /// Adds a middleware instance to the pipeline.
    /// </summary>
    /// <param name="middleware">The middleware instance.</param>
    /// <returns>This builder for chaining.</returns>
    public StateStoreBuilder UseMiddleware(IStateStoreMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Builds an <see cref="IStateStore"/> instance with the configured components.
    /// </summary>
    /// <returns>A configured state store instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required components are not configured.</exception>
    public IStateStore Build()
    {
        var provider = _provider ?? new InMemoryStorageProvider();
        var serializer = _serializer ?? new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline(_middlewares, provider);

        return new StateStoreImplementation(serializer, pipeline);
    }

    /// <summary>
    /// Builds an <see cref="ITypedStateStore{TState}"/> instance with the configured components.
    /// </summary>
    /// <typeparam name="TState">The type of state to manage.</typeparam>
    /// <returns>A configured typed state store instance.</returns>
    public ITypedStateStore<TState> Build<TState>()
    {
        var store = Build();
        return new TypedStateStore<TState>(store);
    }
}
