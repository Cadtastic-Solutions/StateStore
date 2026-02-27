namespace StateStore.Providers.FileSystem;

/// <summary>
/// Configuration options for <see cref="FileSystemStorageProvider"/>.
/// </summary>
public sealed class FileSystemStorageOptions
{
    /// <summary>
    /// Gets or sets the base directory path where state files are stored.
    /// Defaults to a "state" subdirectory in the current working directory.
    /// </summary>
    public string BasePath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "state");

    /// <summary>
    /// Gets or sets the file extension used for state files. Defaults to ".json".
    /// </summary>
    public string FileExtension { get; set; } = ".json";
}
