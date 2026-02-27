using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StateStore.Abstractions;
using StateStore.AutoSave;
using StateStore.Internal;
using StateStore.Middleware;
using StateStore.Options;
using StateStore.Providers.FileSystem;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Extensions;

/// <summary>
/// Extension methods for registering StateStore services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers StateStore services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure StateStore options.</param>
    /// <returns>The service collection for chaining.</returns>
    [RequiresUnreferencedCode("StateStore DI registration uses reflection for middleware and serializer resolution.")]
    [RequiresDynamicCode("StateStore DI registration may require runtime code generation.")]
    public static IServiceCollection AddStateStore(
        this IServiceCollection services,
        Action<StateStoreOptions>? configure = null)
    {
        var options = new StateStoreOptions();
        configure?.Invoke(options);

        // Register options.
        services.Configure<JsonStateSerializerOptions>(o =>
        {
            o.WriteIndented = options.Serializer.WriteIndented;
            o.PropertyNamingPolicy = options.Serializer.PropertyNamingPolicy;
            o.DefaultIgnoreCondition = options.Serializer.DefaultIgnoreCondition;
            o.CustomSerializerOptions = options.Serializer.CustomSerializerOptions;
        });

        if (options.FileSystem is not null)
        {
            services.Configure<FileSystemStorageOptions>(o =>
            {
                o.BasePath = options.FileSystem.BasePath;
                o.FileExtension = options.FileSystem.FileExtension;
            });
        }

        // Register serializer.
        services.TryAddSingleton<IStateSerializer, JsonStateSerializer>();

        // Register storage provider.
        switch (options.Provider)
        {
            case StorageProviderKind.InMemory:
                services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>();
                break;
            case StorageProviderKind.FileSystem:
            default:
                if (options.FileSystem is null)
                {
                    services.Configure<FileSystemStorageOptions>(_ => { });
                }

                services.TryAddSingleton<IStorageProvider, FileSystemStorageProvider>();
                break;
        }

        // Register middleware components via factory to avoid trim warnings.
        RegisterMiddleware(services, options.MiddlewareTypes);

        // Register dirty key tracker.
        services.TryAddSingleton<IDirtyKeyTracker, DirtyKeyTracker>();

        // Register middleware pipeline.
        services.TryAddSingleton(sp =>
        {
            var middlewares = sp.GetServices<IStateStoreMiddleware>().ToList();
            var provider = sp.GetRequiredService<IStorageProvider>();
            return new MiddlewarePipeline(middlewares, provider);
        });

        // Register core state store.
        services.TryAddSingleton<IStateStore>(sp =>
        {
            var serializer = sp.GetRequiredService<IStateSerializer>();
            var pipeline = sp.GetRequiredService<MiddlewarePipeline>();
            var dirtyTracker = sp.GetService<IDirtyKeyTracker>();
            return new StateStoreImplementation(serializer, pipeline, dirtyTracker);
        });

        // Register typed state store as open generic.
        services.TryAddSingleton(typeof(ITypedStateStore<>), typeof(TypedStateStore<>));

        // Register auto-save if configured.
        if (options.AutoSaveConfigurations.Count > 0)
        {
            services.AddSingleton<IEnumerable<IAutoSaveStrategy>>(sp =>
            {
                var builder = new AutoSaveOptionsBuilder();
                foreach (var config in options.AutoSaveConfigurations)
                {
                    config(sp, builder);
                }

                return builder.Build(sp);
            });

            services.AddHostedService<AutoSaveHostedService>();
        }

        return services;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Middleware types are user-provided at registration time and preserved by the caller.")]
    private static void RegisterMiddleware(IServiceCollection services, IReadOnlyList<Type> middlewareTypes)
    {
        foreach (var middlewareType in middlewareTypes)
        {
            services.AddSingleton(typeof(IStateStoreMiddleware), middlewareType);
        }
    }
}
