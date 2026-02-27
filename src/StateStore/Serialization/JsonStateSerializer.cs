using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StateStore.Abstractions;
using StateStore.Exceptions;

namespace StateStore.Serialization;

/// <summary>
/// Default serializer implementation using <see cref="System.Text.Json"/>.
/// For AOT scenarios, consumers should provide a custom <see cref="IStateSerializer"/>
/// implementation using source-generated <c>JsonSerializerContext</c>.
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="JsonStateSerializer"/> with direct options.
/// </remarks>
/// <param name="options">The serializer configuration.</param>
[RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
[RequiresDynamicCode("JSON serialization may require runtime code generation.")]
public sealed class JsonStateSerializer(JsonStateSerializerOptions options) : IStateSerializer
{
    private readonly JsonSerializerOptions _options = options.BuildSerializerOptions();

    /// <summary>
    /// Initializes a new instance of <see cref="JsonStateSerializer"/> using the options pattern.
    /// </summary>
    /// <param name="options">The serializer configuration.</param>
    public JsonStateSerializer(IOptions<JsonStateSerializerOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="JsonStateSerializer"/> with default settings.
    /// </summary>
    public JsonStateSerializer()
        : this(new JsonStateSerializerOptions())
    {
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, _options);
        }
        catch (Exception ex) when (ex is not StateStoreException)
        {
            throw new StateSerializationException(
                $"Failed to serialize value of type '{typeof(T).FullName}'.",
                typeof(T),
                ex);
        }
    }

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, _options);
        }
        catch (Exception ex) when (ex is not StateStoreException)
        {
            throw new StateSerializationException(
                $"Failed to deserialize value to type '{typeof(T).FullName}'.",
                typeof(T),
                ex);
        }
    }
}
