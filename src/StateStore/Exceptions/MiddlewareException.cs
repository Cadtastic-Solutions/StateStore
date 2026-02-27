namespace StateStore.Exceptions;

/// <summary>
/// Thrown when a middleware component encounters an unhandled error during pipeline execution.
/// </summary>
public class MiddlewareException : StateStoreException
{
    /// <summary>
    /// Gets the type of the middleware component that threw the error.
    /// </summary>
    public Type? MiddlewareType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewareException"/>.
    /// </summary>
    public MiddlewareException()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewareException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MiddlewareException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewareException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public MiddlewareException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MiddlewareException"/> with middleware type context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="middlewareType">The type of the middleware that failed.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public MiddlewareException(string message, Type middlewareType, Exception innerException)
        : base(message, innerException)
    {
        MiddlewareType = middlewareType;
    }
}
