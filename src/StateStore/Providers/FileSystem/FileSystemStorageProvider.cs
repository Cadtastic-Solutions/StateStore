using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StateStore.Abstractions;

namespace StateStore.Providers.FileSystem;

/// <summary>
/// A storage provider that persists each key as a separate file in a configurable directory.
/// Implements atomic writes using the write-to-temp-then-rename pattern to prevent corruption.
/// </summary>
public sealed class FileSystemStorageProvider : IStorageProvider
{
    private readonly FileSystemStorageOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemStorageProvider"/> using the options pattern.
    /// </summary>
    /// <param name="options">The file system storage configuration.</param>
    public FileSystemStorageProvider(IOptions<FileSystemStorageOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemStorageProvider"/> with direct options.
    /// </summary>
    /// <param name="options">The file system storage configuration.</param>
    public FileSystemStorageProvider(FileSystemStorageOptions options)
    {
        _options = options;
        Directory.CreateDirectory(_options.BasePath);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(string key, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(key);
        var tempPath = filePath + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(tempPath, data.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(key);
        TryDeleteFile(filePath);
        return default;
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = GetFilePath(key);
        return new ValueTask<bool>(File.Exists(filePath));
    }

    private string GetFilePath(string key)
    {
        var safeFileName = ConvertKeyToSafeFileName(key);
        return Path.Combine(_options.BasePath, safeFileName + _options.FileExtension);
    }

    /// <summary>
    /// Converts a key to a filesystem-safe file name using SHA256 hashing
    /// for keys containing invalid characters, and direct use for safe keys.
    /// </summary>
    private static string ConvertKeyToSafeFileName(string key)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var needsEncoding = key.AsSpan().IndexOfAny(invalidChars) >= 0 || key.Length > 200;

        if (!needsEncoding)
        {
            return key;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup; do not throw from cleanup paths.
        }
    }
}
