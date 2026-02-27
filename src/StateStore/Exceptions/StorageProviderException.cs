namespace StateStore.Exceptions;

/// <summary>
/// Thrown when a storage provider operation fails.
/// </summary>
public class StorageProviderException : StateStoreException
{
    /// <summary>
    /// Gets the key that was being operated on when the failure occurred.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Gets the name of the operation that failed (e.g., "Read", "Write", "Delete").
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    /// Gets the type of the storage provider that threw the error.
    /// </summary>
    public Type? ProviderType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="StorageProviderException"/>.
    /// </summary>
    public StorageProviderException()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StorageProviderException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public StorageProviderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StorageProviderException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StorageProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StorageProviderException"/> with full context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="key">The key that was being operated on.</param>
    /// <param name="operation">The operation that failed.</param>
    /// <param name="providerType">The type of the storage provider.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StorageProviderException(string message, string key, string operation, Type providerType, Exception innerException)
        : base(message, innerException)
    {
        Key = key;
        Operation = operation;
        ProviderType = providerType;
    }
}
