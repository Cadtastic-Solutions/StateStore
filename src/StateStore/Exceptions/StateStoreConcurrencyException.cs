namespace StateStore.Exceptions;

/// <summary>
/// Thrown when a concurrency-related error occurs, such as lock acquisition timeout or cancellation.
/// </summary>
public class StateStoreConcurrencyException : StateStoreException
{
    /// <summary>
    /// Gets the key that was being accessed when the concurrency error occurred.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreConcurrencyException"/>.
    /// </summary>
    public StateStoreConcurrencyException()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreConcurrencyException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public StateStoreConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreConcurrencyException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StateStoreConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreConcurrencyException"/> with key context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="key">The key that was being accessed.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StateStoreConcurrencyException(string message, string key, Exception innerException)
        : base(message, innerException)
    {
        Key = key;
    }
}
