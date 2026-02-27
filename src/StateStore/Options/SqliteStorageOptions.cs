namespace StateStore.Options;

/// <summary>
/// Configuration options for the SQLite storage provider.
/// </summary>
public sealed class SqliteStorageOptions
{
    /// <summary>
    /// Gets or sets the SQLite connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=state.db";
}
