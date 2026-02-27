using MongoDB.Bson;
using MongoDB.Driver;
using StateStore.Options;
using StateStore.Abstractions;

namespace StateStore.Providers.Mongo;

/// <summary>
/// MongoDB-based storage provider for StateStore.
/// </summary>
/// <summary>
/// Provides a MongoDB-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class MongoStorageProvider : IStorageProvider
{
    /// <summary>
    /// The MongoDB collection used for storing state documents.
    /// </summary>
    private readonly IMongoCollection<BsonDocument> _collection;

    /// <summary>
    /// Initializes a new instance of <see cref="MongoStorageProvider"/> using the specified options.
    /// </summary>
    /// <param name="options">The MongoDB storage options.</param>
    public MongoStorageProvider(MongoStorageOptions options)
        : this(options.ConnectionString, options.DatabaseName, options.CollectionName)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MongoStorageProvider"/> using the specified connection string, database, and collection.
    /// </summary>
    /// <param name="connectionString">The MongoDB connection string.</param>
    /// <param name="databaseName">The MongoDB database name.</param>
    /// <param name="collectionName">The MongoDB collection name.</param>
    public MongoStorageProvider(string connectionString, string databaseName = "StateStore", string collectionName = "StateStore")
    {
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _collection = database.GetCollection<BsonDocument>(collectionName);
    }

    /// <summary>
    /// Reads the value associated with the specified key from the MongoDB collection.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The value as a byte array, or null if not found.</returns>
    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        var doc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return doc? ["value"].AsByteArray;
    }

    /// <summary>
    /// Writes the specified value to the MongoDB collection under the given key.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="data">The value to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        var update = Builders<BsonDocument>.Update.Set("value", data.ToArray());
        await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
    }

    /// <summary>
    /// Deletes the value associated with the specified key from the MongoDB collection.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        await _collection.DeleteOneAsync(filter, cancellationToken);
    }

    /// <summary>
    /// Checks if a value exists for the specified key in the MongoDB collection.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        var count = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return count > 0;
    }
}
