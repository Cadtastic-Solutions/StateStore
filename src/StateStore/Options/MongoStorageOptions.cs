namespace StateStore.Options;

/// <summary>
/// Configuration options for the MongoDB storage provider.
/// </summary>
public sealed class MongoStorageOptions
{
    /// <summary>
    /// Gets or sets the MongoDB connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>
    /// Gets or sets the MongoDB database name.
    /// </summary>
    public string DatabaseName { get; set; } = "StateStore";

    /// <summary>
    /// Gets or sets the MongoDB collection name.
    /// </summary>
    public string CollectionName { get; set; } = "StateStore";
}
