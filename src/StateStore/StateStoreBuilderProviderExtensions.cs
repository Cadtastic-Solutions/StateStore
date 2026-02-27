using StateStore.Providers.Sqlite;
using StateStore.Providers.Mongo;
using StateStore.Options;

namespace StateStore;

/// <summary>
/// Extension methods for configuring StateStoreBuilder with additional storage providers.
/// </summary>
public static class StateStoreBuilderProviderExtensions
{
    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified options.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="options">The SQLite storage options.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, SqliteStorageOptions options)
    {
        return builder.UseProvider(new SqliteStorageProvider(options));
    }

    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified connection string.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, string connectionString)
    {
        return builder.UseProvider(new SqliteStorageProvider(connectionString));
    }

    /// <summary>
    /// Configures the builder to use a MongoDB storage provider with the specified options.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="options">The MongoDB storage options.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseMongo(this StateStoreBuilder builder, MongoStorageOptions options)
    {
        return builder.UseProvider(new MongoStorageProvider(options));
    }

    /// <summary>
    /// Configures the builder to use a MongoDB storage provider with the specified connection string, database, and collection.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="connectionString">The MongoDB connection string.</param>
    /// <param name="databaseName">The MongoDB database name.</param>
    /// <param name="collectionName">The MongoDB collection name.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseMongo(this StateStoreBuilder builder, string connectionString, string databaseName = "StateStore", string collectionName = "StateStore")
    {
        return builder.UseProvider(new MongoStorageProvider(connectionString, databaseName, collectionName));
    }
}
