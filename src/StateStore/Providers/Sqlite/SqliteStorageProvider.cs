using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using StateStore.Options;
using StateStore.Abstractions;

namespace StateStore.Providers.Sqlite;

/// <summary>
/// SQLite-based storage provider for StateStore.
/// </summary>
/// <summary>
/// Provides a SQLite-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class SqliteStorageProvider : IStorageProvider
{
    /// <summary>
    /// The connection string used to connect to the SQLite database.
    /// </summary>
    private readonly string _connectionString;


    /// <summary>
    /// Initializes a new instance of <see cref="SqliteStorageProvider"/> using the specified options.
    /// </summary>
    /// <param name="options">The SQLite storage options.</param>
    public SqliteStorageProvider(SqliteStorageOptions options)
        : this(options.ConnectionString)
    {
    }


    /// <summary>
    /// Initializes a new instance of <see cref="SqliteStorageProvider"/> using the specified connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqliteStorageProvider(string connectionString)
    {
        _connectionString = connectionString;
        EnsureTableExists();
    }

    /// <summary>
    /// Ensures the required table exists in the SQLite database.
    /// </summary>
    private void EnsureTableExists()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS StateStore (
            [Key] TEXT PRIMARY KEY,
            [Value] BLOB
        );";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the value associated with the specified key from the SQLite database.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The value as a byte array, or null if not found.</returns>
    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Value] FROM StateStore WHERE [Key] = @key";
        command.Parameters.AddWithValue("@key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as byte[];
    }

    /// <summary>
    /// Writes the specified value to the SQLite database under the given key.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="data">The value to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO StateStore ([Key], [Value]) VALUES (@key, @value)
            ON CONFLICT([Key]) DO UPDATE SET [Value] = excluded.[Value];";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", data.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the value associated with the specified key from the SQLite database.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StateStore WHERE [Key] = @key";
        command.Parameters.AddWithValue("@key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if a value exists for the specified key in the SQLite database.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM StateStore WHERE [Key] = @key";
        command.Parameters.AddWithValue("@key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }
}
