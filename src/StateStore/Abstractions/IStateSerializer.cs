namespace StateStore.Abstractions;

/// <summary>
/// Defines the contract for serializing and deserializing state values to and from bytes.
/// </summary>
public interface IStateSerializer
{
    /// <summary>
    /// Serializes the specified value to a byte array.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The serialized byte representation.</returns>
    /// <exception cref="Exceptions.StateSerializationException">Thrown when serialization fails.</exception>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a byte span to the specified type.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize to.</typeparam>
    /// <param name="data">The raw bytes to deserialize.</param>
    /// <returns>The deserialized value, or <c>default</c> if deserialization produces null.</returns>
    /// <exception cref="Exceptions.StateSerializationException">Thrown when deserialization fails.</exception>
    T? Deserialize<T>(ReadOnlySpan<byte> data);
}
