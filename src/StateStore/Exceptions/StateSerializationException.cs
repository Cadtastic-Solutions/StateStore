namespace StateStore.Exceptions;

/// <summary>
/// Thrown when serialization or deserialization of a state value fails.
/// </summary>
public class StateSerializationException : StateStoreException
{
    /// <summary>
    /// Gets the target type that failed to serialize or deserialize.
    /// </summary>
    public Type? TargetType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="StateSerializationException"/>.
    /// </summary>
    public StateSerializationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateSerializationException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public StateSerializationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateSerializationException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StateSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="StateSerializationException"/> with type context.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="targetType">The type that failed serialization.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public StateSerializationException(string message, Type targetType, Exception innerException)
        : base(message, innerException)
    {
        TargetType = targetType;
    }
}
