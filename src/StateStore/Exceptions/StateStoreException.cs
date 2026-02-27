namespace StateStore.Exceptions;

/// <summary>
/// Base exception for all StateStore library errors.
/// </summary>
public class StateStoreException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreException"/>.
    /// </summary>
    public StateStoreException()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public StateStoreException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateStoreException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StateStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
